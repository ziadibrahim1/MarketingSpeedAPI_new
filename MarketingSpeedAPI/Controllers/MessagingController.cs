using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System.Text.Json;


namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MessagingController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly RestClient _client;
        private readonly string _apiKey;
        private readonly ILogger<MessagingController> _logger;
        public MessagingController(AppDbContext context, IOptions<WasenderSettings> wasenderOptions)
        {
            _context = context;

            _apiKey = wasenderOptions.Value.ApiKey;
            _client = new RestClient(wasenderOptions.Value.BaseUrl.TrimEnd('/'));
        }



        [HttpPost("send-to-groups/{userId}")]
        public async Task<IActionResult> SendToGroups(ulong userId, [FromBody] SendGroupsRequest req)
        {
            var today = DateTime.UtcNow.Date;

            // التحقق من الاشتراك
            var subscription = await _context.UserSubscriptions
                .Where(s => s.UserId == (int)userId &&
                            s.IsActive &&
                            s.PaymentStatus == "paid" &&
                            s.StartDate <= today &&
                            s.EndDate >= today)
                .OrderByDescending(s => s.EndDate)
                .FirstOrDefaultAsync();

            if (subscription == null)
                return Ok(new { success = false, status = "0", message = "Subscription invalid" });

            // التحقق من حساب الواتساب
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null || string.IsNullOrEmpty(account.WasenderSessionId?.ToString()))
                return Ok(new { success = false, status = "1", message = "No account found" });

            var results = new List<object>();
            var random = new Random();

            string AddRandomDots(string text)
            {
                var options = new[] { "", ".", "..", "..." };
                var dots = options[random.Next(options.Length)];
                return $"{text}{dots}";
            }

            // الرسالة الجديدة
            var newMessage = new Message
            {
                PlatformId = account.PlatformId,
                UserId = (long)userId,
                Title = $"Send to groups {DateTime.UtcNow:yyyyMMddHHmmss}",
                Body = req.Message ?? "",
                Targets = JsonConvert.SerializeObject(req.GroupIds),
                Attachments = (req.ImageUrls == null || req.ImageUrls.Count == 0)
                    ? null
                    : JsonConvert.SerializeObject(req.ImageUrls),
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            };

            string GetMessageTypeFromExtension(string fileUrl)
            {
                var extension = Path.GetExtension(fileUrl).ToLower();
                if (extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp")
                    return "imageUrl";
                else if (extension is ".mp4" or ".mov" or ".avi" or ".mkv")
                    return "videoUrl";
                else
                    return "documentUrl";
            }

            async Task<RestResponse> SendWithRetryAsync(RestRequest request, int maxRetries = 3)
            {
                for (int retries = 0; retries < maxRetries; retries++)
                {
                    var response = await _client.ExecuteAsync(request);
                    if (response.Content == null || !response.Content.Contains("retry_after"))
                        return response;

                    try
                    {
                        var retryAfter = JObject.Parse(response.Content)["retry_after"]?.ToObject<int>() ?? 5;
                        Console.WriteLine($"⏳ Server says wait {retryAfter} sec before retry...");
                        await Task.Delay(retryAfter * 1000);
                    }
                    catch
                    {
                        await Task.Delay(5000);
                    }
                }
                return await _client.ExecuteAsync(request);
            }

            

            async Task SendAndLogAsync(string groupId, Dictionary<string, object?> body, string? fileUrl = null)
            {
                // تحقق من الرسائل المكررة خلال 15 دقيقة
                var fifteenMinutesAgo = DateTime.UtcNow.AddMinutes(-15);
                var existingMessage = await _context.Messages
                    .Where(m => m.UserId == (long)userId &&
                                m.Body == req.Message &&
                                m.CreatedAt >= fifteenMinutesAgo)
                    .FirstOrDefaultAsync();

                Message messageToUse = existingMessage ?? newMessage;

                if (existingMessage == null)
                {
                    _context.Messages.Add(newMessage);
                    await _context.SaveChangesAsync();
                }

                var request = new RestRequest("/api/send-message", Method.Post);
                request.AddHeader("Authorization", $"Bearer {account.AccessToken}");
                request.AddHeader("Content-Type", "application/json");
                request.AddJsonBody(body);

                var response = await SendWithRetryAsync(request);

                string? externalId = null;
                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    try
                    {
                        var json = JObject.Parse(response.Content);
                        externalId = json["data"]?["msgId"]?.ToString();
                    }
                    catch { }
                }

                // انتظار 3 ثواني للتحقق من حالة الرسالة
              

                 
                string status = "unknown";
                if (!string.IsNullOrEmpty(externalId))
                {
                    var infoRequest = new RestRequest($"/api/messages/{externalId}/info", Method.Get);
                    infoRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
                    var infoResponse = await _client.ExecuteAsync(infoRequest);
                    if (infoResponse.IsSuccessful && !string.IsNullOrEmpty(infoResponse.Content))
                    {
                        try
                        {
                            var infoJson = JObject.Parse(infoResponse.Content);
                            status = infoJson["status"]?.ToString() ?? "unknown";
                        }
                        catch { }
                    }
                }

                // تسجيل log الرسالة
                var log = new MessageLog
                {
                    MessageId = messageToUse.Id,
                    Recipient = groupId,
                    PlatformId = account.PlatformId,
                    Status = (status == "1" || status == "2") ? "sent" : "sent",
                    ErrorMessage = (status == "1" || status == "2") ? "Blocked or failed" : null,
                    AttemptedAt = DateTime.UtcNow,
                    ExternalMessageId = externalId
                };
                _context.message_logs.Add(log);

                // تسجيل المجموعة كمحظورة إذا كانت الحالة 1 أو 2
                if (status == "1" || status == "2")
                {
                    var alreadyBlocked = await _context.BlockedGroups
                        .AnyAsync(bg => bg.GroupId == groupId && bg.UserId == (int)userId);
                    if (!alreadyBlocked)
                    {
                        _context.BlockedGroups.Add(new BlockedGroup
                        {
                            GroupId = groupId,
                            UserId = (int)userId
                        });
                    }
                }

                await _context.SaveChangesAsync();

                // إضافة نتيجة لإرجاعها للفلاتر
                results.Add(new
                {
                    groupId,
                    fileUrl,
                    success = status != "1" && status != "2",
                    blocked = status == "1" || status == "2",
                    messageId = messageToUse.Id,
                    externalId
                });
            }

            // تنفيذ الإرسال لكل مجموعة
            foreach (var groupId in req.GroupIds)
            {
                if (req.ImageUrls != null && req.ImageUrls.Count > 0)
                {
                    for (int i = 0; i < req.ImageUrls.Count; i++)
                    {
                        bool isLast = i == req.ImageUrls.Count - 1;
                        var msgTypeStr = GetMessageTypeFromExtension(req.ImageUrls[i]);
                        var body = new Dictionary<string, object?>
                {
                    { "to", groupId },
                    { msgTypeStr, req.ImageUrls[i] }
                };
                        if (isLast && !string.IsNullOrWhiteSpace(req.Message))
                            body.Add("text", AddRandomDots(req.Message));

                        await SendAndLogAsync(groupId, body, req.ImageUrls[i]);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(req.Message))
                {
                    var body = new Dictionary<string, object?>
            {
                { "to", groupId },
                { "text", AddRandomDots(req.Message) }
            };
                    await SendAndLogAsync(groupId, body);
                }

                if (req.GroupIds.Count > 1 && groupId != req.GroupIds.Last())
                {
                    int delay = random.Next(5000, 7001);
                    await Task.Delay(delay);
                }
            }

            if (newMessage.Status == "pending")
                newMessage.Status = "failed";

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                status = "2",
                message = newMessage.Body,
                newMessageId = newMessage.Id,
                results
            });
        }


        [HttpPut("edit-message/{messageId}")]
        public async Task<IActionResult> EditMessage(long messageId, [FromBody] EditMessageRequest req)
        {
            var message = await _context.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
            if (message == null)
                return NotFound(new { success = false, message = "Message not found" });

            message.Body = req.Message;
            _context.Messages.Update(message);

            var logs = await _context.message_logs
                .Where(l => l.MessageId == messageId )
                .ToListAsync();

            foreach (var log in logs)
            {
                var account = await _context.user_accounts
                    .FirstOrDefaultAsync(a => a.UserId == message.UserId && a.PlatformId == log.PlatformId);

                if (account == null || !account.WasenderSessionId.HasValue)
                    continue;

                var request = new RestRequest($"/api/messages/{log.ExternalMessageId}", Method.Put);
                request.AddHeader("Authorization", $"Bearer {account.AccessToken}");
                request.AddHeader("Content-Type", "application/json");
                request.AddJsonBody(new { text = req.Message });

                var response = await _client.ExecuteAsync(request);
                if (!response.IsSuccessful)
                {
                    log.Status = "failed";
                    log.ErrorMessage = response.Content;
                }
                else
                {
                    log.Status = "sent";
                    log.ErrorMessage = null;
                }

                _context.message_logs.Update(log);
                await Task.Delay(5000);

            }

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Message updated for all recipients" });
        }

      
        [HttpDelete("delete-message/{messageId}/{PlatformId}/{UserId}")]
        public async Task<IActionResult> DeleteMessage(long messageId, long UserId, long PlatformId)
        {
            var logs = await _context.message_logs
                .Where(l => l.MessageId == messageId )
                .ToListAsync();
            if (!logs.Any())
                return NotFound(new { success = false, message = "No logs found for this message" });
            foreach (var log in logs)
            {
                var account = await _context.user_accounts
                    .FirstOrDefaultAsync(a => a.UserId == UserId && a.PlatformId == PlatformId);
                if (account == null || !account.WasenderSessionId.HasValue)
                    continue;
                var request = new RestRequest($"/api/messages/{log.ExternalMessageId}", Method.Delete);
                request.AddHeader("Authorization", $"Bearer {account.AccessToken}");
                var response = await _client.ExecuteAsync(request);
                if (!response.IsSuccessful)
                {
                    log.ErrorMessage = response.Content;
                    log.Status = "failed";
                    _context.message_logs.Update(log);
                }
                else
                {
                    _context.message_logs.Remove(log);
                }
                await Task.Delay(5000);

            }

            var remainingLogs = await _context.message_logs.AnyAsync(l => l.MessageId == messageId);
            if (!remainingLogs)
            {
                var message = await _context.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
                if (message != null)
                    _context.Messages.Remove(message);
            }

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Message deleted for all recipients" });
        }


        [HttpGet("check-packege-account/{userId}")]
        public async Task<IActionResult> CheckPackegeAccount(ulong userId)
        {
            var today = DateTime.UtcNow.Date;

            var subscription = await _context.UserSubscriptions
                .Where(s => s.UserId == (int)userId &&
                            s.IsActive &&
                            s.PaymentStatus == "paid" &&
                            s.StartDate <= today &&
                            s.EndDate >= today)
                .OrderByDescending(s => s.EndDate)
                .FirstOrDefaultAsync();

            if (subscription == null)
                return Ok(new { success = false, status = "0", message = "No active subscription" });

            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null )
                return Ok(new { success = true, status = "0", message = "No account found" });

            try
            {
                var request = new RestRequest($"/api/status", Method.Get);
                request.AddHeader("Authorization", $"Bearer {account.AccessToken}");

                var response = await _client.ExecuteAsync(request);

                if (!response.IsSuccessful || string.IsNullOrEmpty(response.Content))
                {
                    return Ok(new { success = false, status = "0", message = "Wasender not reachable" });
                }

                var json = System.Text.Json.JsonDocument.Parse(response.Content);
                var wasenderStatus = json.RootElement.GetProperty("status").GetString();

                if (wasenderStatus != "connected")
                {
                    // ممكن تحدث الحالة في DB لو حابب
                    account.Status = "disconnected";
                    await _context.SaveChangesAsync();

                    return Ok(new { success = false, status = "0", message = "Wasender session not connected" });
                }
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, status = "0", message = "Error connecting to Wasender", error = ex.Message });
            }

            var features = await _context.PackageFeatures
                .Where(f => f.PackageId == subscription.PackageId && f.PlatformId == 1)
                .ToListAsync();

            var usage = await _context.subscription_usage
                .Where(u => u.UserId == (int)userId && u.SubscriptionId == subscription.Id)
                .GroupBy(u => u.featureId)
                .Select(g => new
                {
                    FeatureId = g.Key,
                    TotalMessage = g.Sum(x => x.MessageCount),
                    TotalMedia = g.Sum(x => x.MediaCount)
                })
                .ToListAsync();

            var featuresWithLimits = features.Select(f =>
            {
                var u = usage.FirstOrDefault(x => x.FeatureId == f.Id);
                return new
                {
                    f.Id,
                    f.feature,
                    LimitCount = f.LimitCount,
                    sendingLimit = f.sendingLimit,
                    CurrentMessageUsage = f.LimitCount - u?.TotalMessage  ?? 0,
                    CurrentSendingUsage = u?.TotalMedia  ?? 0,
                    IsMessageLimitExceeded = u != null && u.TotalMessage > f.LimitCount,
                    IsMediaLimitExceeded = u != null && u.TotalMedia > f.sendingLimit
                };
            });

            return Ok(new
            {
                success = true,
                status = "1",
                message = "Ok",
                features = featuresWithLimits
            });
        }


        [HttpGet("groups-with-members/{userId}/{platformId}")]
        public async Task<IActionResult> GetGroupsWithMembers(ulong userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == platformId);

            if (account == null || account.WasenderSessionId == null)
                return NotFound();

            // 1) هات المجموعات
            var groupsRequest = new RestRequest($"/api/groups", Method.Get);
            groupsRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            var groupsResp = await _client.ExecuteAsync(groupsRequest);

            if (!groupsResp.IsSuccessful)
                return StatusCode((int)groupsResp.StatusCode, groupsResp.Content);

            var result = new Dictionary<string, List<string>>();

            var groupsJson = JsonDocument.Parse(groupsResp.Content);
            if (groupsJson.RootElement.TryGetProperty("data", out var groups))
            {
                foreach (var g in groups.EnumerateArray())
                {
                    var groupName = g.GetProperty("name").GetString() ?? "Unnamed Group";
                    var groupId = g.GetProperty("id").GetString();

                    // 2) هات تفاصيل الجروب (metadata)
                    var metadataRequest = new RestRequest($"/api/groups/{groupId}/metadata", Method.Get);
                    metadataRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
                    var metadataResp = await _client.ExecuteAsync(metadataRequest);

                    var membersList = new List<string>();
                    if (metadataResp.IsSuccessful)
                    {
                        var metadataJson = JsonDocument.Parse(metadataResp.Content);
                        if (metadataJson.RootElement.TryGetProperty("data", out var metadata) &&
                            metadata.TryGetProperty("participants", out var participants))
                        {
                            foreach (var p in participants.EnumerateArray())
                            {
                                var jid = p.TryGetProperty("jid", out var j) ? j.GetString() : null;

                                if (!string.IsNullOrEmpty(jid))
                                {
                                   
                                    var phone = jid.Contains("@") ? jid.Split('@')[0] : jid;
                                    membersList.Add(phone);
                                }
                            }

                        }
                    }

                    result[groupName] = membersList;
                }
            }

            return Ok(result);
        }

        [HttpGet("groups/{userId}/{platformId}")]
        public async Task<IActionResult> GetGroups(ulong userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == platformId);

            if (account == null || account.WasenderSessionId == null)
                return NotFound();

            var groupsRequest = new RestRequest("/api/groups", Method.Get);
            groupsRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            var groupsResp = await _client.ExecuteAsync(groupsRequest);

            if (!groupsResp.IsSuccessful)
                return StatusCode((int)groupsResp.StatusCode, groupsResp.Content);

            var groupsJson = JsonDocument.Parse(groupsResp.Content);
            var result = new List<object>();

            if (groupsJson.RootElement.TryGetProperty("data", out var groups))
            {
                result = groups.EnumerateArray()
                    .Select(g => new
                    {
                        id = g.GetProperty("id").GetString(),
                        name = g.GetProperty("name").GetString() ?? "Unnamed Group"
                    })
                    .ToList<object>();
            }

            return Ok(result);
        }

        [HttpGet("groups/{userId}/{platformId}/{groupId}/membersCount")]
        public async Task<IActionResult> GetGroupMembersCount(ulong userId, int platformId, string groupId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == platformId);

            if (account == null || account.WasenderSessionId == null)
                return NotFound();

            var metaRequest = new RestRequest($"/api/groups/{groupId}/metadata", Method.Get);
            metaRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            var metaResp = await _client.ExecuteAsync(metaRequest);

            if (!metaResp.IsSuccessful)
                return StatusCode((int)metaResp.StatusCode, metaResp.Content);

            var metaJson = JsonDocument.Parse(metaResp.Content);
            int memberCount = 0;

            if (metaJson.RootElement.TryGetProperty("data", out var meta) &&
                meta.TryGetProperty("participants", out var participants))
            {
                memberCount = participants.GetArrayLength();
            }

            return Ok(new { groupId, membersCount = memberCount });
        }

        [HttpGet("groups/membersCount/{userId}/{platformId}")]
        public async Task<IActionResult> GetGroupsMembersCount(ulong userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == platformId);

            if (account == null || account.WasenderSessionId == null)
                return NotFound();

            // أولاً جلب كل الجروبات
            var groupsRequest = new RestRequest("/api/groups", Method.Get);
            groupsRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            var groupsResp = await _client.ExecuteAsync(groupsRequest);

            if (!groupsResp.IsSuccessful)
                return StatusCode((int)groupsResp.StatusCode, groupsResp.Content);

            var groupsJson = JsonDocument.Parse(groupsResp.Content);
            var groupIds = new List<string>();

            if (groupsJson.RootElement.TryGetProperty("data", out var groups))
            {
                groupIds = groups.EnumerateArray()
                    .Select(g => g.GetProperty("id").GetString())
                    .Where(id => id != null)
                    .Select(id => id!)
                    .ToList();
            }

            var semaphore = new SemaphoreSlim(5); // عدد الـ requests المتوازية
            var tasks = groupIds.Select(async groupId =>
            {
                await semaphore.WaitAsync();
                try
                {
                    int memberCount = 0;
                    try
                    {
                        var metaRequest = new RestRequest($"/api/groups/{groupId}/metadata", Method.Get);
                        metaRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
                        var metaResp = await _client.ExecuteAsync(metaRequest);

                        if (metaResp.IsSuccessful)
                        {
                            var metaJson = JsonDocument.Parse(metaResp.Content);
                            if (metaJson.RootElement.TryGetProperty("data", out var meta) &&
                                meta.TryGetProperty("participants", out var participants))
                            {
                                memberCount = participants.GetArrayLength();
                            }
                        }
                    }
                    catch { }

                    return new
                    {
                        id = groupId,
                        membersCount = memberCount
                    };
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var result = (await Task.WhenAll(tasks)).ToList();
            return Ok(result);
        }


        [HttpGet("group-members/{userId}/{groupJid}")]
        public async Task<IActionResult> GetGroupMembersByGroupJid(ulong userId, string groupJid)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1);

            if (account == null || account.WasenderSessionId == null)
                return NotFound(new { success = false, message = "No active WhatsApp session" });

            var request = new RestRequest($"/api/groups/{groupJid}/participants", Method.Get);
            request.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            var resp = await _client.ExecuteAsync(request);

            if (!resp.IsSuccessful)
                return StatusCode((int)resp.StatusCode, resp.Content);

            var membersList = new List<object>();
            var membersJson = JsonDocument.Parse(resp.Content);

            if (membersJson.RootElement.TryGetProperty("data", out var members))
            {
                foreach (var m in members.EnumerateArray())
                {
                    var id = m.GetProperty("id").GetString();
                    var jid = m.GetProperty("jid").GetString();
                    var lid = m.GetProperty("lid").GetString();
                    var admin = m.TryGetProperty("admin", out var adminProp) && adminProp.ValueKind != JsonValueKind.Null
                                ? adminProp.GetString()
                                : null;

                    // استخرج الرقم فقط من id (قبل @)
                    var numberOnly = jid?.Split('@')[0];

                    membersList.Add(new
                    {
                        Number = numberOnly,
                        Id = id,
                        Jid = jid,
                        Lid = lid,
                        Admin = admin
                    });
                }
            }

            return Ok(new { success = true, data = membersList });
        }


        [HttpGet("chats/{userId}/{platformId}")]
        public async Task<IActionResult> GetChats(ulong userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == platformId);

            if (account == null || account.WasenderSessionId == null)
                return NotFound();

            var request = new RestRequest($"/api/contacts", Method.Get);
            request.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            var resp = await _client.ExecuteAsync(request);

            if (!resp.IsSuccessful)
                return StatusCode((int)resp.StatusCode, resp.Content);

            var data = JsonConvert.DeserializeObject<object>(resp.Content!);
            return Ok(data);
        }
        [HttpGet("get-chats/{userId}")]
        public async Task<IActionResult> GetChats(long userId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, message = "No connected account found" });

            try
            {
                var request = new RestRequest($"/api/contacts", Method.Get);
                request.AddHeader("Authorization", $"Bearer {account.AccessToken}");

                var response = await _client.ExecuteAsync(request);

                if (!response.IsSuccessful || string.IsNullOrEmpty(response.Content))
                    return Ok(new { success = false, message = "Failed to fetch contacts from Wasender" });

                var root = JsonDocument.Parse(response.Content).RootElement;

                JsonElement arrayElement = default;
                bool haveArray = false;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    arrayElement = root;
                    haveArray = true;
                }
                else if (root.TryGetProperty("data", out var dataProp))
                {
                    if (dataProp.ValueKind == JsonValueKind.Array)
                    {
                        arrayElement = dataProp;
                        haveArray = true;
                    }
                    else if (dataProp.ValueKind == JsonValueKind.Object &&
                             dataProp.TryGetProperty("data", out var innerData) &&
                             innerData.ValueKind == JsonValueKind.Array)
                    {
                        arrayElement = innerData;
                        haveArray = true;
                    }
                }

                if (!haveArray)
                {
                    return Ok(new { success = true, message = "Chats fetched successfully", data = Array.Empty<object>() });
                }

                var allowedPrefixes = new List<string>
{
    "20",   // مصر
    "966",  // السعودية
    "971",  // الإمارات
    "974",  // قطر
    "973",  // البحرين
    "965",  // الكويت
    "968",  // عمان
    "967",  // اليمن
    "962",  // الأردن
    "961",  // لبنان
    "963",  // سوريا
    "964",  // العراق
    "970",  // فلسطين
    "249",  // السودان
    "218",  // ليبيا
    "213",  // الجزائر
    "212",  // المغرب
    "216",  // تونس
    "222",  // موريتانيا
    "252",  // الصومال
    "253",  // جيبوتي
    "269"   // جزر القمر
};


                var validChats = new List<JsonElement>();

                foreach (var item in arrayElement.EnumerateArray())
                {
                    string? rawJid = null;
                    if (item.TryGetProperty("jid", out var jidProp) && jidProp.ValueKind == JsonValueKind.String)
                        rawJid = jidProp.GetString();
                    else if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                        rawJid = idProp.GetString();

                    if (string.IsNullOrEmpty(rawJid))
                        continue;

                    var phonePart = rawJid.Contains('@')
                        ? rawJid.Substring(0, rawJid.IndexOf('@'))
                        : rawJid;

                    var clean = new string(phonePart.Where(char.IsDigit).ToArray());

                    if (string.IsNullOrEmpty(clean)) continue;
                    if (!clean.All(char.IsDigit)) continue;
                    if (clean.Length < 8 || clean.Length > 15) continue;

                    // التحقق من أن الرقم يبدأ بأحد المفاتيح المسموحة
                    if (!allowedPrefixes.Any(prefix => clean.StartsWith(prefix)))
                        continue;

                    validChats.Add(item);
                }


                return Ok(new
                {
                    success = true,
                    message = "Chats fetched successfully",
                    data = validChats
                });
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, message = "Error fetching chats", error = ex.Message });
            }
        }

        [HttpPost("send-to-members")]
        public async Task<IActionResult> SendToMembers([FromBody] SendMembersRequest req)
        {
            if (req.Recipients == null || req.Recipients.Count == 0)
                return BadRequest(new { success = false, message = "No recipients provided" });

            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)req.UserId &&
                                          a.PlatformId == req.PlatformId &&
                                          a.Status == "connected");

            if (account == null || string.IsNullOrEmpty(account.WasenderSessionId?.ToString()))
                return Ok(new { success = false, status = "1", message = "No account found" });

            var uniqueRecipients = req.Recipients.Distinct().ToList();

            var newMessage = new Message
            {
                PlatformId = req.PlatformId,
                UserId = (long)req.UserId,
                Title = $"Send to members {DateTime.UtcNow:yyyyMMddHHmmss}",
                Body = req.Message,
                Targets = JsonConvert.SerializeObject(uniqueRecipients),
                Attachments = (req.ImageUrls == null || req.ImageUrls.Count == 0)
                    ? null
                    : JsonConvert.SerializeObject(req.ImageUrls),
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            };

            _context.Messages.Add(newMessage);
            await _context.SaveChangesAsync();

            var results = new List<object>();
            var logs = new List<MessageLog>();

            string GetRandomDotSuffix()
            {
                var options = new[] { "", ".", "..", "..." };
                return options[Random.Shared.Next(options.Length)];
            }

            string CleanText(string text)
            {
                return text.TrimEnd('.', '…', '!', '?', '؟');
            }

            int GetRandomDelayMillis(int minSec = 5, int maxSec = 8)
            {
                return Random.Shared.Next(minSec, maxSec + 1) * 1000;
            }

            foreach (var number in uniqueRecipients)
            {
                var groupLogs = new List<MessageLog>();
                var groupResults = new List<object>();

                string? finalText = null;
                if (!string.IsNullOrWhiteSpace(req.Message))
                {
                    finalText = CleanText(req.Message) + GetRandomDotSuffix();
                }

                if (req.ImageUrls != null && req.ImageUrls.Count > 0)
                {
                    var lastUrl = req.ImageUrls.Last();

                    foreach (var img in req.ImageUrls)
                    {
                        var body = new Dictionary<string, object?> { { "to", number } };

                        var ext = Path.GetExtension(img).ToLower();
                        string msgType = ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp"
                            ? "imageUrl"
                            : ext is ".mp4" or ".mov" or ".avi" or ".mkv"
                                ? "videoUrl"
                                : "documentUrl";

                        body[msgType] = img;

                        if (img == lastUrl && finalText != null)
                            body["text"] = finalText;

                        var request = new RestRequest("/api/send-message", Method.Post);
                        request.AddHeader("Authorization", $"Bearer {account.AccessToken}");
                        request.AddHeader("Content-Type", "application/json");
                        request.AddJsonBody(body);

                        var response = await _client.ExecuteAsync(request);

                        string? externalId = null;
                        if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                        {
                            try
                            {
                                var json = JObject.Parse(response.Content);
                                externalId = json["data"]?["msgId"]?.ToString();
                            }
                            catch { }
                        }

                        groupLogs.Add(new MessageLog
                        {
                            MessageId = newMessage.Id,
                            Recipient = number,
                            PlatformId = account.PlatformId,
                            Status = response.IsSuccessful ? "sent" : "failed",
                            ErrorMessage = response.IsSuccessful ? null : response.Content,
                            AttemptedAt = DateTime.UtcNow,
                            ExternalMessageId = externalId,
                            toGroupMember=true
                        });

                        groupResults.Add(new
                        {
                            number,
                            success = response.IsSuccessful,
                            externalId,
                            error = response.IsSuccessful ? null : response.Content
                        });
                    }
                }
                else if (finalText != null)
                {
                    var body = new Dictionary<string, object?> { { "to", number }, { "text", finalText } };

                    var request = new RestRequest("/api/send-message", Method.Post);
                    request.AddHeader("Authorization", $"Bearer {account.AccessToken}");
                    request.AddHeader("Content-Type", "application/json");
                    request.AddJsonBody(body);

                    var response = await _client.ExecuteAsync(request);

                    string? externalId = null;
                    if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                    {
                        try
                        {
                            var json = JObject.Parse(response.Content);
                            externalId = json["data"]?["msgId"]?.ToString();
                        }
                        catch { }
                    }

                    groupLogs.Add(new MessageLog
                    {
                        MessageId = newMessage.Id,
                        Recipient = number,
                        PlatformId = account.PlatformId,
                        Status = response.IsSuccessful ? "sent" : "failed",
                        ErrorMessage = response.IsSuccessful ? null : response.Content,
                        AttemptedAt = DateTime.UtcNow,
                        ExternalMessageId = externalId,
                        toGroupMember = true
                    });

                    groupResults.Add(new
                    {
                        number,
                        success = response.IsSuccessful,
                        externalId,
                        error = response.IsSuccessful ? null : response.Content
                    });
                }

                logs.AddRange(groupLogs);
                results.AddRange(groupResults);

                await Task.Delay(GetRandomDelayMillis());
            }


            _context.message_logs.AddRange(logs);

            newMessage.Status = logs.Any(l => l.Status == "sent") ? "sent" : "failed";
            newMessage.SentAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                status = "2",
                message = newMessage.Body,
                newMessageId = newMessage.Id,
                results
            });
        }

        [HttpPost("block-chats/{userId}")]
        public async Task<IActionResult> BlockChats(long userId, [FromBody] List<string> phones)
        {
            if (phones == null || phones.Count == 0)
                return BadRequest(new { success = false, message = "No phones provided" });

            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, message = "No connected account found" });

            var results = new List<object>();

            foreach (var phone in phones)
            {
                try
                {
                    // استدعاء Wasender API لحظر الرقم
                    var request = new RestRequest($"/api/contacts/{phone}/block", Method.Post);
                    request.AddHeader("Authorization", $"Bearer {account.AccessToken}");

                    var response = await _client.ExecuteAsync(request);

                    if (!response.IsSuccessful)
                    {
                        results.Add(new { phone, success = false, error = response.Content });
                        continue;
                    }
                    var exists = await _context.blocked_chats
                        .AnyAsync(b => b.UserId == userId && b.Phone == phone);

                    if (!exists)
                    {
                        var blocked = new BlockedChat
                        {
                            UserId = userId,
                            Phone = phone,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow

                        };
                        _context.blocked_chats.Add(blocked);
                        await _context.SaveChangesAsync();
                    }

                    results.Add(new { phone, success = true });
                }
                catch (Exception ex)
                {
                    results.Add(new { phone, success = false, error = ex.Message });
                }
            }

            return Ok(new
            {
                success = true,
                message = "Processed block requests",
                results
            });
        }


        [HttpPost("unblock-chat/{userId}")] 
        public async Task<IActionResult> UnblockChat(long userId, [FromBody] BlockedChat req)
        {
            var phone = req.Phone;

            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, message = "No connected account found" });

            var request = new RestRequest($"/api/contacts/{phone}/unblock", Method.Post);
            request.AddHeader("Authorization", $"Bearer {account.AccessToken}");

            var response = await _client.ExecuteAsync(request);

            if (!response.IsSuccessful)
                return Ok(new { success = false, message = "Failed to unblock", error = response.Content });

            var blocked = await _context.blocked_chats
                .FirstOrDefaultAsync(b => b.UserId == userId && b.Phone == phone);

            if (blocked != null)
            {
                _context.blocked_chats.Remove(blocked);
                await _context.SaveChangesAsync();
            }

            return Ok(new { success = true, message = "Chat unblocked successfully" });
        }

        [HttpGet("blocked-chats/{userId}")]
        public async Task<IActionResult> GetBlockedChats(long userId)
        {
            var blocked = await _context.blocked_chats
                .Where(b => b.UserId == userId)
                .Select(b => new { b.Phone, b.CreatedAt,b.UpdatedAt,b.UserId,b.Id})
                .ToListAsync();

            return Ok(new { success = true, data = blocked });
        }

        [HttpPost("create-group-from-multiple/{userId}")]
        public async Task<IActionResult> CreateGroupFromMultiple(long userId, [FromBody] CreateGroupRequest req)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, message = "No connected account found" });

            var participants = new HashSet<string>();

            foreach (var groupJid in req.SourceGroupIds)
            {
                var metadataRequest = new RestRequest($"/api/groups/{groupJid}/metadata", Method.Get);
                metadataRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");

                var metadataResponse = await _client.ExecuteAsync(metadataRequest);
                if (!metadataResponse.IsSuccessful)
                {
                    return Ok(new { success = false, message = $"Failed to fetch metadata of group {groupJid}", error = metadataResponse.Content });
                }

                var json = JsonDocument.Parse(metadataResponse.Content);
                if (json.RootElement.TryGetProperty("success", out var successProp) && successProp.GetBoolean() &&
                    json.RootElement.TryGetProperty("data", out var data))
                {
                    if (data.TryGetProperty("participants", out var participantsArray))
                    {
                        foreach (var p in participantsArray.EnumerateArray())
                        {
                            var jid = p.GetProperty("jid").GetString();
                            if (!string.IsNullOrEmpty(jid))
                            {
                                participants.Add(jid);  
                            }
                        }
                    }
                }
            }
            var createRequest = new RestRequest("/api/groups", Method.Post);
            createRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            createRequest.AddHeader("Content-Type", "application/json");

            var body = new
            {
                name = req.Name,
                participants = participants.ToArray()
            };
            createRequest.AddJsonBody(body);

            var createResponse = await _client.ExecuteAsync(createRequest);
            if (!createResponse.IsSuccessful)
                return Ok(new { success = false, message = "Failed to create new group", error = createResponse.Content });

            return Ok(new { success = true, message = "Group created successfully", data = createResponse.Content });
        }

    }
}
