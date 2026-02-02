using JamesWright.SimpleHttp;
using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Org.BouncyCastle.Asn1.Cmp.Challenge;

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
        private readonly IServiceProvider _serviceProvider;
        private readonly IMemoryCache _cache;

        
        public MessagingController(AppDbContext context, IOptions<WasenderSettings> wasenderOptions, IServiceProvider serviceProvider, IMemoryCache cache)
        {
            _context = context;
            _apiKey = wasenderOptions.Value.ApiKey;
            _client = new RestClient(wasenderOptions.Value.BaseUrl.TrimEnd('/'));
            _serviceProvider = serviceProvider;
            _cache = cache;

        }



        [HttpPost("send-to-groups/{userId}")]
        public async Task<IActionResult> SendToGroups(ulong userId, [FromBody] SendGroupsRequest req)
        {
           
            var today = DateTime.Now.Date;
            var isSubscribed = await _context.UserSubscriptions
                .AnyAsync(s => s.UserId == (int)userId && s.IsActive && s.PaymentStatus == "paid" && s.StartDate <= today && s.EndDate >= today);

            if (!isSubscribed)
                return Ok(new { success = false, status = "0", message = "Subscription invalid" });

            var account = await _context.user_accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null || string.IsNullOrEmpty(account.WasenderSessionId?.ToString()))
                return Ok(new { success = false, status = "1", message = "No account found" });

      
            var uniqueGroupIds = new HashSet<string>(req.GroupIds).ToList();

            var blockedGroupIds = await _context.BlockedGroups
                .Where(bg => bg.UserId == (int)userId && uniqueGroupIds.Contains(bg.GroupId))
                .Select(bg => bg.GroupId)
                .ToHashSetAsync();

            var groupsToSend = uniqueGroupIds.Except(blockedGroupIds).ToList();
            if (!groupsToSend.Any())
                return Ok(new { success = true, message = "All provided groups are already blocked or the list is empty.", results = new List<object>() });

            var newMessage = new Message
            {
                PlatformId = account.PlatformId,
                UserId = (long)userId,
                Title = $"Send to groups {DateTime.Now:yyyyMMddHHmmss}",
                Body = req.Message ?? "", 
                Targets = JsonConvert.SerializeObject(groupsToSend),
                Attachments = (req.ImageUrls == null || !req.ImageUrls.Any()) ? null : JsonConvert.SerializeObject(req.ImageUrls),
                Status = "pending",
                CreatedAt = DateTime.Now
            };
            _context.Messages.Add(newMessage);
            await _context.SaveChangesAsync(); 

           
            var allResults = new List<object>();
            var allLogs = new List<MessageLog>();
            var newBlockedGroups = new List<BlockedGroup>();

            int consecutiveFailures = 0;
            int sentInCurrentBatch = 0;

            
            foreach (var groupId in groupsToSend)
            {
                
                if (consecutiveFailures >= WhatsAppSafetySettings.MaxConsecutiveFailures)
                {
                    break; 
                }

              
                var (groupResults, groupLogs, wasBlocked) = await ProcessSingleGroupWithCautionAsync(groupId, req, account, newMessage.Id);

                allResults.AddRange(groupResults);
                allLogs.AddRange(groupLogs);

       
                if (wasBlocked)
                {
                    consecutiveFailures++;
                    if (!blockedGroupIds.Contains(groupId))
                    {
                        newBlockedGroups.Add(new BlockedGroup { GroupId = groupId, UserId = (int)userId });
                        blockedGroupIds.Add(groupId);
                    }
                }
                else
                {
                    consecutiveFailures = 0; 
                }

                sentInCurrentBatch++;

             
                if (groupId != groupsToSend.Last())
                {
                    if (sentInCurrentBatch >= WhatsAppSafetySettings.GroupsPerBatch)
                    {
                        await Task.Delay(WhatsAppSafetySettings.BatchRestSeconds * 1000);
                        sentInCurrentBatch = 0;
                    }
                    else
                    {
                        int delay = Random.Shared.Next(WhatsAppSafetySettings.MinDelaySeconds * 1000, WhatsAppSafetySettings.MaxDelaySeconds * 1000);
                        await Task.Delay(delay);
                    }
                }
            }

            
            if (allLogs.Any())
                _context.message_logs.AddRange(allLogs);
            if (newBlockedGroups.Any())
                _context.BlockedGroups.AddRange(newBlockedGroups);

            newMessage.Status = allLogs.Any(l => l.Status == "sent") ? "sent" : "failed";
            newMessage.SentAt = DateTime.Now;

            await _context.SaveChangesAsync();
           
            return Ok(new
            {
                success = true,
                status = "2",
                message = newMessage.Body,
                newMessageId = newMessage.Id,
                results = allResults
            });
        }   
        private async Task<(List<object> results, List<MessageLog> logs, bool wasBlocked)> ProcessSingleGroupWithCautionAsync(
            string groupId, SendGroupsRequest req, UserAccount account, long messageId)
        {
            var groupLogs = new List<MessageLog>();
            var groupResults = new List<object>();
            bool isGroupBlocked = false;

            
            async Task SendAndTrackAsync(string? mediaUrl, string? text)
            {
                if (isGroupBlocked) return;  

                var (result, log, wasBlocked) = await SendApiRequestAndParseResponseAsync(groupId, account, messageId, mediaUrl, text);
                groupResults.Add(result);
                groupLogs.Add(log);
                if (wasBlocked) isGroupBlocked = true;
            }

            if (req.ImageUrls != null && req.ImageUrls.Any())
            {
                for (int i = 0; i < req.ImageUrls.Count; i++)
                {
                    bool isLastImage = i == req.ImageUrls.Count - 1;
                    string? messageText = (isLastImage && !string.IsNullOrWhiteSpace(req.Message)) ? SpinText(req.Message) : null;

                    await SendAndTrackAsync(req.ImageUrls[i], messageText);
                    if (isGroupBlocked) break;  

                    if (!isLastImage)
                        await Task.Delay(Random.Shared.Next(3000, 6000));  
                }
            }
            else if (!string.IsNullOrWhiteSpace(req.Message))
            {
                await SendAndTrackAsync(null, SpinText(req.Message));
            }

            return (groupResults, groupLogs, isGroupBlocked);
        }
        private async Task<(object result, MessageLog log, bool wasBlocked)> SendApiRequestAndParseResponseAsync(
            string groupId, UserAccount account, long messageId, string? mediaUrl, string? text)
        {
            var body = new Dictionary<string, object?> { { "to", groupId } };
            if (mediaUrl != null) body[GetMessageTypeFromExtension(mediaUrl)] = mediaUrl;
            if (text != null) body["text"] = text;

            var request = new RestRequest("/api/send-message", Method.Post);
            request.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            request.AddHeader("Content-Type", "application/json");
            request.AddJsonBody(body);

            var response = await _client.ExecuteAsync(request);

            string? externalId = null;
            bool success = response.IsSuccessful;
            string errorMessage = success ? null : (response.ErrorMessage ?? response.Content);

            bool isBlocked = !success && (
                errorMessage?.Contains("group not found", StringComparison.OrdinalIgnoreCase) == true ||
                errorMessage?.Contains("not a participant", StringComparison.OrdinalIgnoreCase) == true ||
                errorMessage?.Contains("forbidden", StringComparison.OrdinalIgnoreCase) == true
            );

            if (success && !string.IsNullOrEmpty(response.Content))
            {
                try { externalId = JObject.Parse(response.Content)["data"]?["msgId"]?.ToString(); } catch {   }
            }

            var log = new MessageLog
            {
                MessageId = (int)messageId,
                Recipient = groupId,

                PlatformId = account.PlatformId,
                Status = success ? "sent" : "failed",
                ErrorMessage = errorMessage,
                AttemptedAt = DateTime.Now,
                ExternalMessageId = externalId
            };

            var result = new { groupId, success, blocked = isBlocked, externalId };

            return (result, log, isBlocked);
        }

        private string SpinText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var regex = new Regex("{([^{}]*)}");
            string currentText = text;

            while (currentText.Contains("{") && currentText.Contains("}"))
            {
                currentText = regex.Replace(currentText, match => {
                    if (match.Groups.Count < 2) return "";
                    var options = match.Groups[1].Value.Split('|');
                    return options[Random.Shared.Next(options.Length)];
                }, 1);
            }

            var dots = new[] { "", ".", "..", "..." };
            return $"{currentText.Trim()}{dots[Random.Shared.Next(dots.Length)]}";
        }

        private string GetMessageTypeFromExtension(string fileUrl)
        {
            var extension = Path.GetExtension(fileUrl).ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => "imageUrl",
                ".mp4" or ".mov" or ".avi" or ".mkv" => "videoUrl",
                _ => "documentUrl"
            };
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
                await Task.Delay(7000);

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

        [HttpGet("subscription/has/{userId}")]
        public async Task<IActionResult> HasSubscription(int userId)
        {
            var today = DateTime.Now.Date;

            bool hasSubscription = await _context.UserSubscriptions.AnyAsync(s =>
                s.UserId == userId &&
                s.IsActive &&
                s.PaymentStatus == "paid" &&
                s.StartDate <= today &&
                s.EndDate >= today
            );

            return Ok(new
            {
                success = true,
                hasSubscription
            });
        }

        [HttpGet("connection/status/{userId}")]
        public async Task<IActionResult> CheckConnection(int userId)
        {
            var account = await _context.user_accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == userId && a.PlatformId == 1);

            if (account == null || string.IsNullOrEmpty(account.AccessToken))
            {
                return Ok(new
                {
                    success = true,
                    isConnected = false
                });
            }

            bool isConnected = false;

            try
            {
                var client = new RestClient("https://gate.whapi.cloud/users/profile");
                var request = new RestRequest();
                request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                var res = await client.GetAsync(request);
                isConnected = res.IsSuccessful;
            }
            catch { }

            return Ok(new
            {
                success = true,
                isConnected
            });
        }

        [HttpGet("subscription/features/{userId}")]
        public async Task<IActionResult> GetSubscriptionFeatures(int userId)
        {
            var today = DateTime.Now.Date;

            var subscriptions = await _context.UserSubscriptions
                .Where(s => s.UserId == userId &&
                            s.IsActive &&
                            s.PaymentStatus == "paid" &&
                            s.StartDate <= today &&
                            s.EndDate >= today)
                .ToListAsync();

            if (!subscriptions.Any())
                return Ok(new { success = true, features = new List<object>() });

            var packageIds = subscriptions.Select(s => s.PackageId).Distinct().ToList();
            var subscriptionIds = subscriptions.Select(s => s.Id).ToList();

            var features = await _context.PackageFeatures
                .Where(f => packageIds.Contains(f.PackageId) && f.PlatformId == 1 && f.isMain)
                .ToListAsync();

            var usages = await _context.subscription_usage
                .Where(u => u.UserId == userId && subscriptionIds.Contains(u.SubscriptionId))
                .ToListAsync();

            var result = new List<object>();

            foreach (var sub in subscriptions)
            {
                foreach (var feature in features.Where(f => f.PackageId == sub.PackageId))
                {
                    var usage = usages.FirstOrDefault(u =>
                        u.SubscriptionId == sub.Id && u.FeatureId == feature.Id);

                    int used = usage?.UsedCount ?? 0;
                    int remaining = Math.Max(feature.LimitCount - used, 0);

                    result.Add(new
                    {
                        feature.FeatureEn,
                        feature.feature,
                        feature.forMembers,
                        feature.forCreatingGroups,
                        feature.forGetingGruops,
                        LimitCount = feature.LimitCount,
                        UsedCount = used,
                        RemainingCount = remaining
                    });
                }
            }

            return Ok(new
            {
                success = true,
                features = result
            });
        }


        [HttpGet("check-packege-account/{userId}")]
        public async Task<IActionResult> CheckPackegeAccount(ulong userId)
        {
            var today = DateTime.Now.Date;

            var subscriptions = await _context.UserSubscriptions
                .Where(s => s.UserId == (int)userId &&
                            s.IsActive &&
                            s.PaymentStatus == "paid" &&
                            s.StartDate <= today &&
                            s.EndDate >= today)
                .OrderByDescending(s => s.EndDate)
                .AsNoTracking()
                .ToListAsync();

            bool hasSubscription = subscriptions.Count > 0;
            bool isConnected = false;

            if (!hasSubscription)
            {
                return Ok(new
                {
                    success = true,
                    isConnected = false,
                    hasSubscription = false,
                    message = "No active subscription",
                    features = new List<object>()
                });
            }

            // 🔹 الحساب في DB
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1);

            if (account == null || string.IsNullOrEmpty(account.AccessToken))
            {
                return Ok(new
                {
                    success = true,
                    isConnected = false,
                    hasSubscription = true,
                    message = "No account found",
                    features = new List<object>()
                });
            }

            // ================================
            //     🔍 التحقق من اتصال WHAPI
            // ================================
            bool whapiConnected = false;

            try
            {
                var client = new RestClient(new RestClientOptions("https://gate.whapi.cloud/users/profile"));
                var request = new RestRequest("");
                request.AddHeader("accept", "application/json");
                request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                var response = await client.GetAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.OK &&
                    !string.IsNullOrEmpty(response.Content) &&
                    response.Content.Contains("name"))
                {
                    whapiConnected = true;
                }
            }
            catch
            {
                whapiConnected = false;
            }

            // ================================
            //   لو WHAPI مفصول → حدث DB
            // ================================
            if (!whapiConnected)
            {
                try
                {
                    account.Status = "disconnected";
                
                    // account.WasenderSessionId = null;

                    _context.user_accounts.Update(account);
                    await _context.SaveChangesAsync();
                }
                catch { }

                return Ok(new
                {
                    success = true,
                    isConnected = false,
                    hasSubscription = true,
                    message = "WHAPI session not connected",
                    features = new List<object>()
                });
            }

            // هنا WHAPI فعلاً متصل
            isConnected = true;

            // 🔹 نحدث DB لو غير متصل قبل كده
            if (account.Status != "connected")
            {
                account.Status = "connected";
                _context.user_accounts.Update(account);
                await _context.SaveChangesAsync();
            }

            // ====================================================
            //     
            // ====================================================
            var packageIds = subscriptions.Select(s => s.PackageId).Distinct().ToList();

            var features = await _context.PackageFeatures
                .Where(f => packageIds.Contains(f.PackageId) && f.PlatformId == 1 && f.isMain == true)
                .AsNoTracking()
                .ToListAsync();
            

            var subscriptionIds = subscriptions.Select(s => s.Id).ToList();

            var usages = await _context.subscription_usage
                .Where(u => u.UserId == (int)userId && subscriptionIds.Contains(u.SubscriptionId))
                .AsNoTracking()
                .ToListAsync();

            var grants = await _context.subscription_feature_grants
                .Where(g =>
                g.UserId == userId &&
                subscriptionIds.Contains((int)g.SubscriptionId) &&
                g.IsActive &&
                (g.EndDate == null || g.EndDate > DateTime.Now)
                 )
                .AsNoTracking()
                .ToListAsync();

            var result = new List<object>();
            foreach (var sub in subscriptions)
            {
                var subFeatures = features.Where(f => f.PackageId == sub.PackageId).ToList();
                foreach (var feature in subFeatures)
                {
                    var usage = usages.FirstOrDefault(u => u.SubscriptionId == sub.Id && u.FeatureId == feature.Id);

                    int used = usage?.UsedCount ?? 0;

                    // 🔹 limit الأساسي من الباقة
                    int baseLimit = feature.LimitCount;

                    // 🔹 مجموع الهدايا النشطة
                    int extraGranted = grants
                        .Where(g =>
                            g.SubscriptionId == sub.Id &&
                            g.FeatureId == (uint)feature.Id
                        )
                        .Sum(g => Math.Max(g.GrantedCount - g.UsedCount, 0));

                    // 🔹 limit النهائي
                    int finalLimit = baseLimit + extraGranted;

                    // 🔹 المتبقي
                    int remaining = Math.Max(finalLimit - used, 0);


                    result.Add(new
                    {
                        SubscriptionId = sub.Id,
                        PackageId = sub.PackageId,
                        sub.PlanName,
                        sub.Price,
                        sub.StartDate,
                        sub.EndDate,

                        FeatureId = feature.Id,
                        FeatureAr = feature.feature,
                        FeatureEn = feature.FeatureEn,
                        forMembers = feature.forMembers,
                        forCreatingGroups = feature.forCreatingGroups,
                        forGetingGroups = feature.forGetingGruops,

                        LimitCount = feature.LimitCount,   
                        UsedCount = used,
                        RemainingCount = remaining,       
                        IsExceeded = remaining <= 0
                    });

                }
            }

            return Ok(new
            {
                success = true,
                isConnected,
                hasSubscription,
                message = "Connected account with active subscriptions",
                features = result
            });
        }

        [HttpGet("groups-with-members/{userId}/{platformId}")]
        public async Task<IActionResult> GetGroupsWithMembers(ulong userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == platformId);

            if (account == null || account.WasenderSessionId == null)
                return NotFound();

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

            if (account == null || string.IsNullOrEmpty(account.AccessToken))
                return NotFound(new { success = false, message = "No active session" });

            
            var leftJids = await _context.LeftGroups
                .Where(lg => lg.UserId == (int)userId)
                .Select(lg => lg.GroupId)
                .ToListAsync();

            var result = new List<object>();

            int offset = 0;
            int total = 0;

            do
            {
                var client = new RestClient($"https://gate.whapi.cloud/groups?count=500&offset={offset}");
                var request = new RestRequest("", Method.Get);
                request.AddHeader("accept", "application/json");
                request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                    return StatusCode((int)response.StatusCode, response.Content);

                var json = JObject.Parse(response.Content);

                var groupsArray = json["groups"]?.ToArray();
                total = json["total"]?.ToObject<int>() ?? 0;

                if (groupsArray != null)
                {
                    foreach (var g in groupsArray)
                    {
                        string id = g["id"]?.ToString();
                        string name = g["name"]?.ToString();
                        string participantCountStr = g["participants_count"]?.ToString();

                        if (string.IsNullOrEmpty(id))
                            continue;

                        // ❌ لا ترجع المجموعات التي غادرها المستخدم
                        if (leftJids.Contains(id))
                            continue;

                        // ❌ تجاهل المجموعات من نوع Restricted + Announcements
                        bool isRestricted = g["restricted"]?.ToObject<bool>() ?? false;
                        bool isAnnouncements = g["announcements"]?.ToObject<bool>() ?? false;
                        bool isCommunityAnnounce = g["isCommunityAnnounce"]?.ToObject<bool>() ?? false;
                        bool not_spam = g["not_spam"]?.ToObject<bool>() ?? false;
                        if (isRestricted && isAnnouncements || isCommunityAnnounce || not_spam==false)
                            continue;

                        result.Add(new
                        {
                            id = id,
                            name = name,
                            participantCount = participantCountStr != null ? int.Parse(participantCountStr) : 0
                        });
                    }
                }

                offset += 500;

            } while (offset < total);

            // إزالة التكرارات كما في الكود القديم
            result = result
                .GroupBy(g => g.GetType().GetProperty("id").GetValue(g))
                .Select(g => g.First())
                .ToList();

            return Ok(result);
        }

        [HttpGet("create_groups/{userId}/{platformId}")]
        public async Task<IActionResult> GetGroupsCreate(ulong userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == platformId);

            if (account == null || string.IsNullOrEmpty(account.AccessToken))
                return NotFound(new { success = false, message = "No active session" });

            string Normalize(string n) => string.IsNullOrWhiteSpace(n) ? "" : n.Replace("+", "").Trim();
            string myNumber = Normalize(account.AccountIdentifier);

            var leftJids = await _context.LeftGroups
                .Where(l => l.UserId == (int)userId)
                .Select(l => l.GroupId)
                .ToHashSetAsync();  

            var adminGroups = new List<object>();
            var memberGroups = new List<object>();

            int offset = 0;
            int total;

            do
            {
                var client = new RestClient($"https://gate.whapi.cloud/groups?count=500&offset={offset}");
                var request = new RestRequest();
                request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                var response = await client.ExecuteAsync(request);
                if (!response.IsSuccessful)
                    return StatusCode((int)response.StatusCode, response.Content);

                var json = JObject.Parse(response.Content);
                var groups = json["groups"] as JArray;
                total = json["total"]?.ToObject<int>() ?? 0;

                foreach (var g in groups ?? Enumerable.Empty<JToken>())
                {
                    string id = g["id"]?.ToString();
                    if (string.IsNullOrEmpty(id) || leftJids.Contains(id))
                        continue;

                    bool isRestricted = g["restricted"]?.ToObject<bool>() ?? false;
                    bool isAnnouncements = g["announcements"]?.ToObject<bool>() ?? false;
                    bool isCommunityAnnounce = g["isCommunityAnnounce"]?.ToObject<bool>() ?? false;
                    bool notSpam = g["not_spam"]?.ToObject<bool>() ?? false;

                    if ((isRestricted && isAnnouncements) || isCommunityAnnounce || !notSpam)
                        continue;

                    string name = g["name"]?.ToString();
                    int count = g["participants_count"]?.ToObject<int>() ?? 0;

                    bool isAdmin = false;

                    if (Normalize(g["created_by"]?.ToString()) == myNumber)
                    {
                        isAdmin = true;
                    }
                    else
                    {
                        var participants = g["participants"] as JArray;
                        if (participants != null)
                        {
                            foreach (var p in participants)
                            {
                                if (Normalize(p["id"]?.ToString()) == myNumber)
                                {
                                    var rank = p["rank"]?.ToString();
                                    isAdmin = rank == "admin" || rank == "creator";
                                    break; // ⛔ وقف فورًا
                                }
                            }
                        }
                    }

                    var obj = new { id, name, participantCount = count };

                    if (isAdmin)
                        adminGroups.Add(obj);
                    else
                        memberGroups.Add(obj);
                }

                offset += 500;

            } while (offset < total);

            return Ok(new
            {
                adminGroups,
                memberGroups
            });
        }


        [HttpGet("groupsdelte/{userId}/{platformId}")]
        public async Task<IActionResult> GetGroupsForDelete(ulong userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == platformId);

            if (account == null || string.IsNullOrEmpty(account.AccessToken))
                return NotFound(new { success = false, message = "No active session" });

            var result = new List<object>();

            int offset = 0;
            int total = 0;

            do
            {
                var client = new RestClient($"https://gate.whapi.cloud/groups?count=500&offset={offset}");
                var request = new RestRequest("", Method.Get);
                request.AddHeader("accept", "application/json");
                request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                    return StatusCode((int)response.StatusCode, response.Content);

                var json = JObject.Parse(response.Content);

                var groupsArray = json["groups"]?.ToArray();
                total = json["total"]?.ToObject<int>() ?? 0;

                if (groupsArray != null)
                {
                    foreach (var g in groupsArray)
                    {
                        string id = g["id"]?.ToString();
                        string name = g["name"]?.ToString();
                        string participantCountStr = g["participants_count"]?.ToString();

                        if (string.IsNullOrEmpty(id))
                            continue;
                         
                        // ❌ تجاهل المجموعات من نوع Restricted + Announcements
                        bool isRestricted = g["restricted"]?.ToObject<bool>() ?? false;
                        bool isAnnouncements = g["announcements"]?.ToObject<bool>() ?? false;
                        bool isCommunityAnnounce = g["isCommunityAnnounce"]?.ToObject<bool>() ?? false;
                        bool not_spam = g["not_spam"]?.ToObject<bool>() ?? false;
                        if (isRestricted && isAnnouncements || isCommunityAnnounce || not_spam == false)
                            continue;

                        result.Add(new
                        {
                            id = id,
                            name = name,
                            participantCount = participantCountStr != null ? int.Parse(participantCountStr) : 0
                        });
                    }
                }

                offset += 500;

            } while (offset < total);

            // إزالة التكرارات كما في الكود القديم
            result = result
                .GroupBy(g => g.GetType().GetProperty("id").GetValue(g))
                .Select(g => g.First())
                .ToList();

            return Ok(result);
        }

        [HttpGet("groups-member/{userId}/{platformId}")]
        public async Task<IActionResult> GetMemberGroups(ulong userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == platformId);

            if (account == null || string.IsNullOrEmpty(account.AccessToken))
                return NotFound(new { success = false, message = "No active session" });

            var leftJids = await _context.LeftGroups
                .Where(lg => lg.UserId == (int)userId)
                .Select(lg => lg.GroupId)
                .ToListAsync();

            var result = new List<object>();

            int offset = 0;
            int total = 0;

            do
            {
                var client = new RestClient($"https://gate.whapi.cloud/groups?count=500&offset={offset}");
                var request = new RestRequest("", Method.Get);
                request.AddHeader("accept", "application/json");
                request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                    return StatusCode((int)response.StatusCode, response.Content);

                var json = JObject.Parse(response.Content);
                var groupsArray = json["groups"]?.ToArray();
                total = json["total"]?.ToObject<int>() ?? 0;

                if (groupsArray != null)
                {
                    foreach (var g in groupsArray)
                    {
                        string id = g["id"]?.ToString();
                        string name = g["name"]?.ToString();

                        if (string.IsNullOrEmpty(id))
                            continue;

                        if (leftJids.Contains(id))
                            continue;

                        bool isCommunityAnnounce = g["isCommunityAnnounce"]?.ToObject<bool>() ?? false;
                        bool not_spam = g["not_spam"]?.ToObject<bool>() ?? false;

                        if (isCommunityAnnounce || not_spam==false)
                            continue;

                        // 🟦 جلب الأعضاء كما جاءت في الرد
                        var participants = g["participants"]?.Select(p => new { id = p["id"]?.ToString(),}).Cast<object>().ToList() ?? new List<object>();


                        result.Add(new
                        {
                            id = id,
                            name = name,
                            participants = participants ?? new List<object>()
                        });
                    }
                }

                offset += 500;

            } while (offset < total);

            // إزالة التكرارات
            result = result
                .GroupBy(g => g.GetType().GetProperty("id")!.GetValue(g))
                .Select(g => g.First())
                .ToList();

            return Ok(result);
        }

        [HttpGet("channel-member/{userId}/{platformId}")]
        public async Task<IActionResult> GetMemberchannel(ulong userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == platformId);

            if (account == null || string.IsNullOrEmpty(account.AccessToken))
                return NotFound(new { success = false, message = "No active session" });

            var leftJids = await _context.LeftGroups
                .Where(lg => lg.UserId == (int)userId)
                .Select(lg => lg.GroupId)
                .ToListAsync();

            var result = new List<object>();

            int offset = 0;
            int total = 0;

            do
            {
                var client = new RestClient($"https://gate.whapi.cloud/groups?count=500&offset={offset}");
                var request = new RestRequest("", Method.Get);
                request.AddHeader("accept", "application/json");
                request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                    return StatusCode((int)response.StatusCode, response.Content);

                var json = JObject.Parse(response.Content);
                var groupsArray = json["groups"]?.ToArray();
                total = json["total"]?.ToObject<int>() ?? 0;

                if (groupsArray != null)
                {
                    foreach (var g in groupsArray)
                    {
                        string id = g["id"]?.ToString();
                        string name = g["name"]?.ToString();

                        if (string.IsNullOrEmpty(id))
                            continue;

                        if (leftJids.Contains(id))
                            continue;

                        bool isCommunityAnnounce = g["isCommunityAnnounce"]?.ToObject<bool>() ?? false;
                        if (!isCommunityAnnounce)
                            continue;

                        // 🟦 جلب الأعضاء كما جاءت في الرد
                        var participants = g["participants"]?.Select(p => new { id = p["id"]?.ToString(), }) .Cast<object>() .ToList()?? new List<object>();


                        result.Add(new
                        {
                            id = id,
                            name = name,
                            participants = participants ?? new List<object>()
                        });
                    }
                }

                offset += 500;

            } while (offset < total);

            // إزالة التكرارات
            result = result
                .GroupBy(g => g.GetType().GetProperty("id")!.GetValue(g))
                .Select(g => g.First())
                .ToList();

            return Ok(result);
        }


        [HttpGet("groups-restricted/{userId}/{platformId}")]
        public async Task<IActionResult> GetRestrictedAnnouncementGroups(ulong userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == platformId);

            if (account == null || string.IsNullOrEmpty(account.AccessToken))
                return NotFound(new { success = false, message = "No active session" });

            // المجموعات التي تركها المستخدم – يتم استبعادها
            var leftJids = await _context.LeftGroups
                .Where(lg => lg.UserId == (int)userId)
                .Select(lg => lg.GroupId)
                .ToListAsync();

            var result = new List<object>();

            int offset = 0;
            int total = 0;

            do
            {
                var client = new RestClient($"https://gate.whapi.cloud/groups?count=500&offset={offset}");
                var request = new RestRequest("", Method.Get);
                request.AddHeader("accept", "application/json");
                request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                    return StatusCode((int)response.StatusCode, response.Content);

                var json = JObject.Parse(response.Content);

                var groupsArray = json["groups"]?.ToArray();
                total = json["total"]?.ToObject<int>() ?? 0;

                if (groupsArray != null)
                {
                    foreach (var g in groupsArray)
                    {
                        string id = g["id"]?.ToString();
                        string name = g["name"]?.ToString();

                        if (string.IsNullOrEmpty(id))
                            continue;

                        // تجاهل المجموعات التي تركها المستخدم
                        if (leftJids.Contains(id))
                            continue;

                        // ✨ الشرط الجديد: إرجاع فقط المجموعات التي فيها الخاصيتين
                        bool restricted = g["restricted"]?.ToObject<bool>() ?? false;
                        bool announcements = g["announcements"]?.ToObject<bool>() ?? false;

                        if (restricted && announcements)
                        {
                            result.Add(new
                            {
                                id = id,
                                name = name
                            });
                        }
                    }
                }

                offset += 500;

            } while (offset < total);

            // إزالة التكرار كما في دالتك الأصلية
            result = result
                .GroupBy(g => g.GetType().GetProperty("id").GetValue(g))
                .Select(g => g.First())
                .ToList();

            return Ok(result);
        }


        [HttpGet("groups/{userId}/{platformId}/{groupId}/membersCount")]
        public async Task<IActionResult> GetGroupMembersCount(ulong userId, int platformId, string groupId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == platformId);

            if (account == null || account.AccessToken == null)
                return NotFound(new { success = false, message = "No active WhatsApp session" });

            // WHAPI endpoint
            var options = new RestClientOptions($"https://gate.whapi.cloud/groups/{groupId}?resync=false");
            var client = new RestClient(options);

            var request = new RestRequest("", Method.Get);
            request.AddHeader("accept", "application/json");
            request.AddHeader("authorization", $"Bearer {account.AccessToken}");

            var response = await client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                return StatusCode((int)response.StatusCode, response.Content);
            }

            var json = JsonDocument.Parse(response.Content);

            int memberCount = 0;

            if (json.RootElement.TryGetProperty("participants", out var participants))
            {
                memberCount = participants.GetArrayLength();
            }

            return Ok(new
            {
                groupId,
                membersCount = memberCount
            });
        }


        [HttpGet("groups/membersCount/{userId}/{platformId}")]
        public async Task<IActionResult> GetGroupsMembersCount(ulong userId, int platformId)
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
            var groupIds = new List<string>();

            if (groupsJson.RootElement.TryGetProperty("data", out var groups))
            {
                groupIds = groups.EnumerateArray()
                    .Select(g => g.GetProperty("id").GetString())
                    .Where(id => id != null)
                    .Select(id => id!)
                    .ToList();
            }

            var semaphore = new SemaphoreSlim(5); 
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

            if (account == null || string.IsNullOrEmpty(account.AccessToken))
                return NotFound(new { success = false, message = "No active WhatsApp session" });

            // WHAPI Endpoint
            var options = new RestClientOptions($"https://gate.whapi.cloud/groups/{groupJid}?resync=true");
            var client = new RestClient(options);

            var request = new RestRequest("", Method.Get);
            request.AddHeader("accept", "application/json");
            request.AddHeader("authorization", $"Bearer {account.AccessToken}");

            // تنفيذ الطلب
            var response = await client.ExecuteAsync(request);

            if (!response.IsSuccessful)
                return StatusCode((int)response.StatusCode, response.Content);

            var membersList = new List<object>();

            using var json = JsonDocument.Parse(response.Content);

            // WHAPI: participants[]
            if (json.RootElement.TryGetProperty("participants", out var participants))
            {
                foreach (var p in participants.EnumerateArray())
                {
                    string id = p.GetProperty("id").GetString();   // رقم بدون @
                    string rank = p.TryGetProperty("rank", out var r) ? r.GetString() : null;

                    // عمل JID مثل النظام القديم
                    string jid = $"{id}@g.us";

                    membersList.Add(new
                    {
                        Number = id,
                        Id = id,
                        Jid = jid,
                        Admin = rank // "admin" أو "member"
                    });
                }
            }

            return Ok(new
            {
                success = true,
                data = membersList
            });
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
                .FirstOrDefaultAsync(a =>
                    a.UserId == (int)userId &&
                    a.PlatformId == 1 &&
                    a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, message = "No connected account found" });

            try
            {
                // الأكواد العربية المسموح بها
                var allowedPrefixes = new List<string>
        {
            "20","966","971","974","973","965","968","967","962",
            "961","963","964","970","249","218","213","212","216",
            "222","252","253","269"
        };

                var collectedContacts = new List<JsonElement>();
                int offset = 0;
                int limit = 500;

                while (true)
                {
                    var restClient = new RestClient(
                        $"https://gate.whapi.cloud/contacts?count={limit}&offset={offset}"
                    );

                    var request = new RestRequest("", Method.Get);
                    request.AddHeader("accept", "application/json");
                    request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                    var response = await restClient.ExecuteAsync(request);

                    if (!response.IsSuccessful || string.IsNullOrEmpty(response.Content))
                        break;

                    using var json = JsonDocument.Parse(response.Content);

                    if (!json.RootElement.TryGetProperty("contacts", out var contactsArray) ||
                        contactsArray.ValueKind != JsonValueKind.Array)
                        break;

                    // ✅ الحل هنا: Clone()
                    foreach (var contact in contactsArray.EnumerateArray())
                    {
                        collectedContacts.Add(contact.Clone());
                    }

                    int count = json.RootElement.GetProperty("count").GetInt32();
                    int total = json.RootElement.GetProperty("total").GetInt32();

                    offset += count;
                    if (offset >= total)
                        break;
                }

                // 🔽 نفس منطق الفلترة القديم
                var validChats = new List<object>();

                foreach (var item in collectedContacts)
                {
                    if (!item.TryGetProperty("id", out var idProp))
                        continue;

                    var phone = idProp.GetString();
                    if (string.IsNullOrWhiteSpace(phone))
                        continue;

                    var clean = new string(phone.Where(char.IsDigit).ToArray());
                    if (clean.Length < 8 || clean.Length > 15)
                        continue;

                    if (!allowedPrefixes.Any(prefix => clean.StartsWith(prefix)))
                        continue;

                    validChats.Add(new
                    {
                        id = phone + "@c.us",     // نفس صيغة chats
                        type = "contact",
                        name = item.TryGetProperty("name", out var nameProp)
                            ? nameProp.GetString()
                            : null,
                        pushname = item.TryGetProperty("pushname", out var pushProp)
                            ? pushProp.GetString()
                            : null,
                        saved = item.TryGetProperty("saved", out var savedProp)
                            ? savedProp.GetBoolean()
                            : false
                    });
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
                return Ok(new
                {
                    success = false,
                    message = "Error fetching chats",
                    error = ex.Message
                });
            }
        }


        [HttpGet("get-personal-chats/{userId}")]
        public async Task<IActionResult> GetPersonalChats(long userId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, message = "No connected account found" });

            try
            {
                // الأكواد العربية فقط — كما طلبت
                var allowedPrefixes = new List<string>
        {
            "20","966","971","974","973","965","968","967","962",
            "961","963","964","970","249","218","213","212","216",
            "222","252","253","269"
        };

                var contactsList = new List<object>();

                int offset = 0;
                int limit = 300;

                while (true)
                {
                    var client = new RestClient($"https://gate.whapi.cloud/chats?count={limit}&offset={offset}");
                    var request = new RestRequest("", Method.Get);
                    request.AddHeader("accept", "application/json");
                    request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                    var response = await client.ExecuteAsync(request);

                    if (!response.IsSuccessful)
                        break;

                    var json = JsonDocument.Parse(response.Content);

                    if (!json.RootElement.TryGetProperty("chats", out var chatsArray) ||
                        chatsArray.ValueKind != JsonValueKind.Array)
                        break;

                    foreach (var item in chatsArray.EnumerateArray())
                    {
                        if (!item.TryGetProperty("type", out var typeProp)) continue;

                        // 🔥 فلترة الدردشات الفردية فقط
                        if (typeProp.GetString() != "contact") continue;

                        if (!item.TryGetProperty("id", out var idProp)) continue;

                        string jid = idProp.GetString();
                        if (string.IsNullOrEmpty(jid)) continue;

                        string number = jid.Split('@')[0];
                        number = new string(number.Where(char.IsDigit).ToArray());

                        if (number.Length < 8 || number.Length > 15) continue;

                        if (!allowedPrefixes.Any(p => number.StartsWith(p)))
                            continue;

                        string name = "";
                        if (item.TryGetProperty("name", out var nameProp))
                        {
                            name = nameProp.GetString() ?? "";
                        }

                        contactsList.Add(new
                        {
                            phone = number,
                            name = name,
                            jid = jid
                        });
                    }

                    int count = json.RootElement.GetProperty("count").GetInt32();
                    int total = json.RootElement.GetProperty("total").GetInt32();
                    offset += count;
                    if (offset >= total) break;
                }

                return Ok(new
                {
                    success = true,
                    message = "Chats fetched successfully",
                    data = contactsList
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
                Title = $"Send to members {DateTime.Now:yyyyMMddHHmmss}",
                Body = req.Message,
                Targets = JsonConvert.SerializeObject(uniqueRecipients),
                Attachments = (req.ImageUrls == null || req.ImageUrls.Count == 0) ? null : JsonConvert.SerializeObject(req.ImageUrls),
                Status = "pending",
                CreatedAt = DateTime.Now
            };

            _context.Messages.Add(newMessage);
            await _context.SaveChangesAsync();

            var logs = new List<MessageLog>();
            var results = new List<object>();

            string GetRandomDotSuffix()
            {
                var options = new[] { "", ".", "..", ". ." };
                return options[Random.Shared.Next(options.Length)];
            }

            string CleanText(string text)
            {
                return text.TrimEnd('.', '…', '!', '?', '؟');
            }

            int GetRandomDelayMillis(int minSec = 10, int maxSec = 20)
            {
                return Random.Shared.Next(minSec, maxSec + 1) * 1000;
            }

            async Task SendMessageToRecipient(string number)
            {
                string? finalText = null;
                if (!string.IsNullOrWhiteSpace(req.Message))
                {
                    finalText = CleanText(req.Message) + GetRandomDotSuffix();
                }

                var groupLogs = new List<MessageLog>();
                var groupResults = new List<object>();

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
                            UserId = (int)req.UserId,
                            body = req.Message,
                            PlatformId = account.PlatformId,
                            Status = response.IsSuccessful ? "sent" : "failed",
                            ErrorMessage = response.IsSuccessful ? null : response.Content,
                            AttemptedAt = DateTime.Now,
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
                        UserId = (int)req.UserId,
                        body = req.Message,
                        Recipient = number,
                        PlatformId = account.PlatformId,
                        Status = response.IsSuccessful ? "sent" : "failed",
                        ErrorMessage = response.IsSuccessful ? null : response.Content,
                        AttemptedAt = DateTime.Now,
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

            int maxConcurrentSends = 3;
            var semaphore = new SemaphoreSlim(maxConcurrentSends);

            var sendTasks = uniqueRecipients.Select(async number =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await SendMessageToRecipient(number);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(sendTasks);

            _context.message_logs.AddRange(logs);
            newMessage.Status = logs.Any(l => l.Status == "sent") ? "sent" : "failed";
            newMessage.SentAt = DateTime.Now;

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
                .FirstOrDefaultAsync(a => a.UserId == (int)userId &&
                                          a.PlatformId == 1 &&
                                          a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, message = "No connected account found" });

            var results = new List<object>();

            foreach (var phone in phones)
            {
                try
                {
                    // 🔥 WHAPI — النظام الجديد
                    var client = new RestClient($"https://gate.whapi.cloud/blacklist/{phone}");
                    var request = new RestRequest("", Method.Put);
                    request.AddHeader("accept", "application/json");
                    request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                    var response = await client.ExecuteAsync(request);

                    if (!response.IsSuccessful)
                    {
                        results.Add(new { phone, success = false, error = response.Content });
                        continue;
                    }

                    // 🟩 حفظ في blocked_chats كما هو بدون تعديل
                    var exists = await _context.blocked_chats
                        .AnyAsync(b => b.UserId == userId && b.Phone == phone);

                    if (!exists)
                    {
                        var blocked = new BlockedChat
                        {
                            UserId = userId,
                            Phone = phone,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
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
                .FirstOrDefaultAsync(a =>
                    a.UserId == (int)userId &&
                    a.PlatformId == 1 &&
                    a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, message = "No connected account found" });

            try
            {
                // 🔥 WHAPI — endpoint الجديد
                var client = new RestClient($"https://gate.whapi.cloud/blacklist/{phone}");
                var request = new RestRequest("", Method.Delete);

                request.AddHeader("accept", "application/json");
                request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                var response = await client.DeleteAsync(request);

                if (response == null || !response.IsSuccessful)
                {
                    return Ok(new
                    {
                        success = false,
                        message = "Failed to unblock",
                        error = response?.Content
                    });
                }

                // 🗑 حذف من قاعدة البيانات كما هو
                var blocked = await _context.blocked_chats
                    .FirstOrDefaultAsync(b => b.UserId == userId && b.Phone == phone);

                if (blocked != null)
                {
                    _context.blocked_chats.Remove(blocked);
                    await _context.SaveChangesAsync();
                }

                return Ok(new { success = true, message = "Chat unblocked successfully" });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    message = "Exception occurred",
                    error = ex.Message
                });
            }
        }


        [HttpGet("blocked-chats/{userId}")]
        public async Task<IActionResult> GetBlockedChats(long userId)
        {
            // 1️⃣ اجلب بيانات المستخدم للحصول على التوكن
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == userId &&
                                          a.PlatformId == 1 &&
                                          a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, message = "No connected account found" });

            // 2️⃣ استعلام WHAPI
            var client = new RestClient("https://gate.whapi.cloud/blacklist");
            var request = new RestRequest("", Method.Get);

            request.AddHeader("accept", "application/json");
            request.AddHeader("authorization", $"Bearer {account.AccessToken}");

            var response = await client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                return StatusCode((int)response.StatusCode,
                    new { success = false, message = "Failed to fetch blacklist", details = response.Content });
            }

            // 3️⃣ فك الرد من WHAPI
            List<string>? whapiBlockedNumbers;
            try
            {
                whapiBlockedNumbers = System.Text.Json.JsonSerializer.Deserialize<List<string>>(response.Content!);
            }
            catch
            {
                whapiBlockedNumbers = new List<string>();
            }

            // 4️⃣ هات الموجود فعلاً في قاعدة البيانات (للتواريخ فقط)
            var dbBlocked = await _context.blocked_chats
                .Where(b => b.UserId == userId)
                .ToListAsync();

            // 5️⃣ دمج WHAPI + DB في الشكل القديم
            var finalList = whapiBlockedNumbers.Select(phone =>
            {
                var dbEntry = dbBlocked.FirstOrDefault(b => b.Phone == phone);

                return new
                {
                    Phone = phone,
                    UserId = userId,
                    Id = dbEntry?.Id ?? 0, // أو 0 لو مفيش
                    CreatedAt = dbEntry?.CreatedAt ?? DateTime.Now,
                    UpdatedAt = dbEntry?.UpdatedAt ?? DateTime.Now
                };
            }).ToList();

            return Ok(new
            {
                success = true,
                data = finalList
            });
        }

        [HttpPost("save-contact/{userId}")]
        public async Task<IActionResult> SaveContact(ulong userId,[FromBody] SaveContactRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Phone) || string.IsNullOrWhiteSpace(req.Name))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "Phone and name are required"
                });
            }

            // جلب حساب واتساب المتصل
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a =>
                    a.UserId == (int)userId &&
                    a.PlatformId == req.PlatformId &&
                    a.Status == "connected");

            if (account == null)
            {
                return Ok(new
                {
                    success = false,
                    error = "No connected WhatsApp account found"
                });
            }

            var phone = NormalizePhone(req.Phone);

            // إعداد WHAPI
            var client = new RestClient(new RestClientOptions("https://gate.whapi.cloud"));
            var request = new RestRequest("contacts", Method.Put);

            request.AddHeader("accept", "application/json");
            request.AddHeader("authorization", $"Bearer {account.AccessToken}");
            request.AddHeader("content-type", "application/json");

            request.AddJsonBody(new
            {
                phone = phone,
                name = req.Name
            });

            try
            {
                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                {
                    return Ok(new
                    {
                        success = false,
                        error = response.Content ?? response.ErrorMessage
                    });
                }

                return Ok(new
                {
                    success = true,
                    phone,
                    name = req.Name
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }


        string NormalizePhone(string number)
        {
            if (string.IsNullOrWhiteSpace(number))
                return string.Empty;

            var digitsOnly = new string(number.Where(char.IsDigit).ToArray());

            if (digitsOnly.StartsWith("00"))
                digitsOnly = digitsOnly.Substring(2);

            return digitsOnly;
        }

        private static readonly HashSet<string> _usedSuffixes = new();
        private static readonly object _suffixLock = new();

        [HttpPost("send-to-single-member/{userId}")]
        public async Task<IActionResult> SendToSingleMember(ulong userId, [FromBody] SendSingleMemberRequest req)
        {
            if (string.IsNullOrEmpty(req.Recipient))
                return BadRequest(new { success = false, blocked = false, error = "Recipient is required" });

            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId &&
                                          a.PlatformId == req.PlatformId &&
                                          a.Status == "connected");

            if (account == null  )
                return Ok(new { success = false, blocked = false, error = "No connected account found" });

            if (NormalizePhone(account.AccountIdentifier) == NormalizePhone(req.Recipient))
                return Ok(new { success = false, blocked = false, error = "Recipient number matches sender" });

            var senderNumber = NormalizePhone(account.AccountIdentifier);
            var body = new Dictionary<string, object?> { { "to", NormalizePhone(req.Recipient) } };

            if (req.ImageUrls != null && req.ImageUrls.Any())
            {
                var mediaUrl = req.ImageUrls.First();
                body[GetMessageTypeFromExtension(mediaUrl)] = mediaUrl;
                if (req.Message != null) body["text"] = req.Message;
            }
            else if (!string.IsNullOrEmpty(req.Message))
            {
                body["text"] = req.Message;
            }

            if (body.Count <= 1)
                return Ok(new { success = false, blocked = false, error = "Message body and attachments are empty" });
             
            bool success = false;
            bool isBlocked = false;
            string? errorMessage = null;
            string? externalId = null;

            var whapiClient = new RestClient(new RestClientOptions("https://gate.whapi.cloud"));
            RestRequest sendReq;

            string recipient = NormalizePhone(req.Recipient);

            if (req.ImageUrls != null && req.ImageUrls.Any())
            {
                sendReq = new RestRequest("messages/image", Method.Post);
                sendReq.AddHeader("authorization", $"Bearer {account.AccessToken}");
                sendReq.AddHeader("accept", "application/json");
                sendReq.AddHeader("content-type", "application/json");

                sendReq.AddJsonBody(new
                {
                    to = recipient,
                    media = req.ImageUrls.First(),
                    caption = req.Message
                });
            }
            else
            {
                sendReq = new RestRequest("messages/text", Method.Post);
                sendReq.AddHeader("authorization", $"Bearer {account.AccessToken}");
                sendReq.AddHeader("accept", "application/json");
                sendReq.AddHeader("content-type", "application/json");

                sendReq.AddJsonBody(new
                {
                    to = recipient,
                    body = req.Message
                });
            }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                var res = await whapiClient.ExecuteAsync(sendReq);

                if (res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    await Task.Delay(3500);
                    continue;
                }

                success = res.IsSuccessful;
                errorMessage = success ? null : (res.ErrorMessage ?? res.Content);

                if (success)
                {
                    try
                    {
                        var json = JObject.Parse(res.Content);
                        externalId = json["message"]?["id"]?.ToString();
                    }
                    catch { }
                }

                isBlocked =
                    !success &&
                    (
                        (errorMessage?.Contains("Recipient_not_found", StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (errorMessage?.Contains("blocked", StringComparison.OrdinalIgnoreCase) ?? false)
                    );

                break;
            }

            
            try
            {
                var log = new MessageLog
                {
                    MessageId = (int)(req.MainMessageId ?? 0),
                    Recipient = req.Recipient,
                    sender = senderNumber,
                    UserId = (int)userId,
                    body = req.Message,
                    PlatformId = account.PlatformId,
                    Status = success ? "sent" : "failed",
                    ErrorMessage = errorMessage,
                    AttemptedAt = DateTime.Now,
                    ExternalMessageId = externalId
                };

                _context.message_logs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch { }

            if (success)
            {
                try
                {
                    var activeSubs = await _context.UserSubscriptions
                        .Where(s => s.UserId == (int)userId &&
                                    s.IsActive &&
                                    s.PaymentStatus == "paid" &&
                                    s.StartDate <= DateTime.Now &&
                                    s.EndDate >= DateTime.Now)
                        .OrderBy(s => s.StartDate)
                        .ToListAsync();

                    foreach (var sub in activeSubs)
                    {
                        var feature = await _context.PackageFeatures
                            .FirstOrDefaultAsync(f => f.PackageId == sub.PackageId && f.forMembers == true);

                        if (feature == null)
                            continue;

                        var usage = await _context.subscription_usage
                            .FirstOrDefaultAsync(u => u.UserId == (int)userId &&
                                                      u.SubscriptionId == sub.Id &&
                                                      u.FeatureId == feature.Id);

                        if (usage == null)
                        {
                            usage = new SubscriptionUsage
                            {
                                UserId = (int)userId,
                                SubscriptionId = sub.Id,
                                PackageId = sub.PackageId,
                                FeatureId = feature.Id,
                                LimitCount = feature.LimitCount,
                                UsedCount = 1,
                                LastUsedAt = DateTime.Now
                            };
                            _context.subscription_usage.Add(usage);
                            await _context.SaveChangesAsync();
                            break;
                        }
                        else if (usage.LimitCount > usage.UsedCount)
                        {
                            usage.UsedCount += 1;
                            usage.LastUsedAt = DateTime.Now;
                            _context.subscription_usage.Update(usage);
                            await _context.SaveChangesAsync();
                            break;
                        }
                    }
                }
                catch { }
            }

            // ------------------------------------------
            // النتيجة النهائية كما هي
            // ------------------------------------------
            return Ok(new
            {
                success,
                req.MainMessageId,
                blocked = isBlocked,
                error = errorMessage,
                externalId
            });
        }

        [HttpPost("send-to-single-group/{userId}")]
        public async Task<IActionResult> SendToSingleGroup(ulong userId, [FromBody] SendSingleGroupRequest req)
        {
            if (string.IsNullOrEmpty(req.GroupId))
                return BadRequest(new { success = false, blocked = false, error = "GroupId is required" });

            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId &&
                                          a.PlatformId == 1 &&
                                          a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, blocked = false, error = "No connected account found" });

            string? finalText = !string.IsNullOrWhiteSpace(req.Message) ? SpinText(req.Message) : null;
            bool hasAttachment = req.ImageUrls != null && req.ImageUrls.Any();

            bool overallSuccess = true;
            bool blocked = false;
            string? errorMessage = null;
            List<string> externalIds = new List<string>();

            // ----------------------------------------
            //  إرسال الرسائل (نص أو media متعددة)
            // ----------------------------------------
            if (hasAttachment && req.ImageUrls.Count > 0)
            {
                // إرسال كل صورة/ملف في رسالة منفصلة
                for (int i = 0; i < req.ImageUrls.Count; i++)
                {
                    string mediaUrl = req.ImageUrls[i];
                    string ext = Path.GetExtension(mediaUrl).ToLower();
                    string endpoint = "";
                    var body = new Dictionary<string, object?> { { "to", req.GroupId } };

                    // تحديد نوع الملف
                    bool isLastMedia = (i == req.ImageUrls.Count - 1);

                    if (ext is ".jpg" or ".jpeg" or ".png")
                    {
                        endpoint = "messages/image";
                        body["media"] = mediaUrl;
                        // Caption فقط في آخر رسالة
                        if (isLastMedia && finalText != null) body["caption"] = finalText;
                    }
                    else if (ext == ".mp4")
                    {
                        endpoint = "messages/video";
                        body["media"] = mediaUrl;
                        if (isLastMedia && finalText != null) body["caption"] = finalText;
                    }
                    else if (ext == ".pdf" || ext == ".docx")
                    {
                        endpoint = "messages/document";
                        body["media"] = mediaUrl;
                        body["mimetype"] = ext == ".pdf"
                            ? "application/pdf"
                            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                        if (isLastMedia && finalText != null) body["caption"] = finalText;
                    }
                    else
                    {
                        // لو الامتداد مش معروف، نتخطى الملف ده
                        continue;
                    }

                    // إرسال الرسالة
                    var result = await SendMediaMessage(account, endpoint, body);

                    if (!result.success)
                    {
                        overallSuccess = false;
                        errorMessage = result.error;
                        blocked = result.blocked;

                        // لو في blocking أو خطأ حرج، نوقف
                        if (blocked) break;
                    }
                    else if (result.externalId != null)
                    {
                        externalIds.Add(result.externalId);
                    }

                    // تسجيل لوج لآخر رسالة فقط
                    if (isLastMedia)
                    {
                        await LogMessage(
                            userId,
                            (ulong?)req.MainMessageId,
                            req.GroupId,
                            account,
                            req.Message,
                            req.fromChates,
                            result.success,
                            result.error,
                            result.externalId
                        );
                    }

                    // تأخير صغير بين الرسائل (300ms)
                    if (i < req.ImageUrls.Count - 1)
                        await Task.Delay(300);
                }

                // لو في نص ومفيش caption اتحط (يعني كل الملفات فشلت)، نرسل النص في رسالة منفصلة
                if (finalText != null && externalIds.Count == 0)
                {
                    await Task.Delay(300);
                    var textResult = await SendTextMessage(account, req.GroupId, finalText);
                    if (textResult.externalId != null)
                        externalIds.Add(textResult.externalId);

                    // تسجيل لوج للرسالة النصية (لأن مفيش صور اتبعتت)
                    await LogMessage(
                        userId,
                        (ulong?)req.MainMessageId,
                        req.GroupId,
                        account,
                        req.Message,
                        req.fromChates,
                        textResult.success,
                        textResult.error,
                        textResult.externalId
                    );
                }
            }
            else if (!string.IsNullOrEmpty(finalText))
            {
                // رسالة نصية فقط
                var result = await SendTextMessage(account, req.GroupId, finalText);
                overallSuccess = result.success;
                errorMessage = result.error;
                blocked = result.blocked;
                if (result.externalId != null)
                    externalIds.Add(result.externalId);

                // تسجيل لوج للرسالة النصية
                await LogMessage(
                    userId,
                    (ulong?)req.MainMessageId,
                    req.GroupId,
                    account,
                    req.Message,
                    req.fromChates,
                    result.success,
                    result.error,
                    result.externalId
                );
            }

            return Ok(new
            {
                success = overallSuccess,
                req.MainMessageId,
                blocked,
                error = errorMessage,
                externalIds,
                sentCount = externalIds.Count
            });
        }

        // ----------------------------------------
        //       Helper Methods
        // ----------------------------------------
        private async Task<(bool success, bool blocked, string? error, string? externalId)>
            SendMediaMessage(dynamic account, string endpoint, Dictionary<string, object?> body)
        {
            var client = new RestClient(new RestClientOptions("https://gate.whapi.cloud"));
            var sendReq = new RestRequest(endpoint, Method.Post);

            sendReq.AddHeader("authorization", $"Bearer {account.AccessToken}");
            sendReq.AddHeader("accept", "application/json");
            sendReq.AddHeader("content-type", "application/json");
            sendReq.AddJsonBody(body);

            bool success = false;
            bool blocked = false;
            string? errorMessage = null;
            string? externalId = null;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                var res = await client.ExecuteAsync(sendReq);

                if (res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    await Task.Delay(3500);
                    continue;
                }

                success = res.IsSuccessful;
                errorMessage = success ? null : (res.ErrorMessage ?? res.Content);

                if (success)
                {
                    try
                    {
                        var json = JObject.Parse(res.Content);
                        externalId = json["message"]?["id"]?.ToString();
                    }
                    catch { }
                }

                blocked = !success && (
                    (errorMessage?.Contains("Recipient_not_found", StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (errorMessage?.Contains("blocked", StringComparison.OrdinalIgnoreCase) ?? false)
                );

                break;
            }

            return (success, blocked, errorMessage, externalId);
        }

        private async Task<(bool success, bool blocked, string? error, string? externalId)>
            SendTextMessage(dynamic account, string recipient, string text)
        {
            var body = new Dictionary<string, object?>
    {
        { "to", recipient },
        { "body", text }
    };

            return await SendMediaMessage(account, "messages/text", body);
        }

        // ----------------------------------------
        //       تسجيل اللوج لكل رسالة
        // ----------------------------------------
        private async Task LogMessage(
            ulong userId,
            ulong? mainMessageId,
            string groupId,
            dynamic account,
            string? message,
            bool? fromChates,
            bool success,
            string? error,
            string? externalId)
        {
            try
            {
                var log = new MessageLog
                {
                    MessageId = (int)(mainMessageId ?? 0),
                    Recipient = groupId,
                    sender = NormalizePhone(account.AccountIdentifier),
                    UserId = (int)userId,
                    body = message,
                    PlatformId = fromChates == true ? 3 : account.PlatformId,
                    Status = success ? "sent" : "failed",
                    ErrorMessage = error,
                    AttemptedAt = DateTime.Now,
                    ExternalMessageId = externalId
                };

                _context.message_logs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch { }
        }

        [HttpGet("daily-limit/{userId}")]
        public async Task<IActionResult> GetDailyLimit(ulong userId)
        {
            try
            {
                var account = await _context.user_accounts
                    .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.Status == "connected");

                if (account == null)
                {
                    return Ok(new
                    {
                        success = false,
                        message = "No connected account found",
                        dailyLimit = 0,
                        sentToday = 0,
                        remaining = 0,
                        dayInCycle = 0
                    });
                }

                string senderNumber = NormalizePhone(account.AccountIdentifier);
                DateTime today = DateTime.Now.Date;

                // أول مرة يستخدم اليوم
                if (account.CurrentDayInCycle == 0 || account.LastActiveDate == null)
                {
                    account.CurrentDayInCycle = 1;
                    account.LastActiveDate = today;
                    await _context.SaveChangesAsync();

                    return await CalculateCycleResult(senderNumber, 1);
                }

                int currentDay = (int)account.CurrentDayInCycle;
                DateTime lastActive = account.LastActiveDate.Value.Date;

                int gapDays = (today - lastActive).Days;

                // ❶ الغياب يومين ⇒ العودة لليوم 1
                if (gapDays >= 2)
                {
                    account.CurrentDayInCycle = 1;
                    account.LastActiveDate = today;
                    await _context.SaveChangesAsync();

                    return await CalculateCycleResult(senderNumber, 1);
                }

                // ❷ gapDays = 1 ⇒ تحقق 90% من يوم أمس
                if (gapDays == 1)
                {
                    int yesterdayLimit = GetLimitForDay(currentDay);

                    DateTime yStart = today.AddDays(-1);
                    DateTime yEnd = today;

                    int sentYesterday = await _context.message_logs.CountAsync(m =>
                        m.sender == senderNumber &&
                        m.AttemptedAt >= yStart &&
                        m.AttemptedAt < yEnd &&
                        !m.Recipient.EndsWith("@g.us")
                    );

                    bool reached90 = sentYesterday >= (int)(yesterdayLimit * 0.9);

                    if (reached90)
                        currentDay++; // نزيد اليوم

                    account.CurrentDayInCycle = currentDay;
                    account.LastActiveDate = today;
                    await _context.SaveChangesAsync();

                    return await CalculateCycleResult(senderNumber, currentDay);
                }

                // ❸ gapDays = 0 ⇒ المستخدم نشط اليوم ⇒ لا يزيد اليوم
                account.LastActiveDate = today;
                await _context.SaveChangesAsync();

                return await CalculateCycleResult(senderNumber, currentDay);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error in GetDailyLimit: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal Server Error"
                });
            }
        }

        private async Task<int> GetLastDayInCycle(string sender)
        {
            var firstLog = await _context.message_logs
                .Where(m => m.sender == sender)
                .OrderBy(m => m.AttemptedAt)
                .FirstOrDefaultAsync();

            if (firstLog == null) return 1;

            int daysPassed = (DateTime.Now.Date - firstLog.AttemptedAt.Date).Days;

            return (daysPassed % 30) + 1;
        }

        private async Task<IActionResult> CalculateCycleResult(string senderNumber, int dayInCycle)
        {
            int dailyLimit = GetLimitForDay(dayInCycle);

            DateTime today = DateTime.Now.Date;

            int sentToday = await _context.message_logs.CountAsync(m =>
                m.sender == senderNumber &&
                m.AttemptedAt >= today &&
                !m.Recipient.EndsWith("@g.us")
            );

            int remaining = (dailyLimit == int.MaxValue)
                ? int.MaxValue
                : Math.Max(0, dailyLimit - sentToday);

            return new JsonResult(new
            {
                success = true,
                dailyLimit = (dailyLimit == int.MaxValue) ? 999999 : dailyLimit,
                sentToday,
                remaining = (remaining == int.MaxValue) ? 999999 : remaining,
                dayInCycle
            });
        }

        private int GetLimitForDay(int day)
        {
            return day switch
            {
                <= 3 => 500,
                <= 6 => 500,
                <= 9 => 500,
                _ => 500
            };
        }



        [HttpPost("create-group-from-multiple/{userId}")]
        public async Task<IActionResult> CreateGroupFromMultiple(long userId, [FromBody] CreateGroupRequest req)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, message = "No connected account found" });

            if (string.IsNullOrWhiteSpace(req.Name))
                return Ok(new { success = false, message = "Group name is required" });

            // 🔹 check if subscription exists and active
            var existingSubscription = await _context.group_subscriptions
                .FirstOrDefaultAsync(g => g.UserId == userId && g.GroupId == req.Name && g.Status == "active");

            string groupJid = "";

            if (existingSubscription != null)
            {
                // group already exists
                groupJid = existingSubscription.GroupId;
            }
            else
            {
                // 🆕 ---------- WHAPI GROUP CREATE ----------
                var options = new RestClientOptions("https://gate.whapi.cloud/groups");
                var client = new RestClient(options);

                // participants = رقم بدون أي suffix
                var members = req.Members?.Take(2).ToArray() ?? Array.Empty<string>();

                var request = new RestRequest("", Method.Post);
                request.AddHeader("accept", "application/json");
                request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                request.AddJsonBody(new
                {
                    participants = members,
                    subject = req.Name        // مهم: WHAPI يستخدم subject بدل name
                });

                var response = await client.PostAsync(request);

                if (!response.IsSuccessful)
                {
                    return Ok(new
                    {
                        success = false,
                        message = "Failed to create group",
                        error = response.Content
                    });
                }

                var json = JsonDocument.Parse(response.Content);
                groupJid = json.RootElement.GetProperty("group_id").GetString();

                if (string.IsNullOrEmpty(groupJid))
                    return Ok(new { success = false, message = "Invalid WHAPI response" });

                // 🔹 Save to DB
                var sub = new GroupSubscription
                {
                    UserId = (int)userId,
                    GroupId = groupJid,
                    StartDate = DateTime.Now,
                    EndDate = DateTime.Now.AddMonths(1),
                    Status = "active",
                    LastBatchTime = DateTime.Now
                };

                _context.group_subscriptions.Add(sub);
                await _context.SaveChangesAsync();

                // 🔹 Deduct usage
                await DeductUsageForCreatingGroupsAsync((int)userId, 2);
            }

            return Ok(new
            {
                success = true,
                message = "Group created or fetched successfully",
                groupId = groupJid
            });
        }

        [HttpPost("add-group-members/{userId}")]
        public async Task<IActionResult> AddGroupMembers(long userId, [FromBody] AddGroupMembersRequest req)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId &&
                                          a.PlatformId == 1 &&
                                          a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, message = "No connected account found" });

            if (string.IsNullOrWhiteSpace(req.GroupId) || req.Members == null || req.Members.Count == 0)
                return Ok(new { success = false, message = "Invalid request" });

            var results = new List<object>();
            int successCount = 0;

            foreach (var member in req.Members)
            {
                try
                {
                    // 🔥 WHAPI — endpoint الجديد
                    var client = new RestClient($"https://gate.whapi.cloud/groups/{req.GroupId}/participants");
                    var request = new RestRequest("", Method.Post);

                    request.AddHeader("accept", "application/json");
                    request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                    // لا تضيف أي suffix — ترسل الرقم فقط
                    request.AddJsonBody(new
                    {
                        participants = new List<string> { member }
                    });

                    var response = await client.ExecuteAsync(request);
                    bool success = response.IsSuccessful;
                    string? errorMessage = success ? null : response.Content;

                    results.Add(new
                    {
                        member,
                        success,
                        error = errorMessage
                    });

                    if (success)
                    {
                        successCount++;

                        // خصم الاستخدام كما هو
                        await DeductUsageForCreatingGroupsAsync((int)userId, 1);
                    }

                    // ⏳ الانتظار العشوائي كما في كودك القديم
                    int delaySec = Random.Shared.Next(10, 21);
                    await Task.Delay(delaySec * 1000);
                }
                catch (Exception ex)
                {
                    results.Add(new
                    {
                        member,
                        success = false,
                        error = ex.Message
                    });
                }
            }

            // تحديث batch time كما هو تماماً
            try
            {
                var sub = await _context.group_subscriptions
                    .FirstOrDefaultAsync(g => g.GroupId == req.GroupId);

                if (sub != null)
                {
                    sub.LastBatchTime = DateTime.Now;
                    _context.group_subscriptions.Update(sub);
                    await _context.SaveChangesAsync();
                }
            }
            catch { }

            return Ok(new
            {
                success = true,
                message = $"Members processed (added one by one). Total successful: {successCount}",
                results
            });
        }

        /// ✅ دالة خصم الاستخدام بدقة لكل عضو ناجح
        private async Task DeductUsageForCreatingGroupsAsync(int userId, int count)
        {
            try
            {
                // 🧩 اجلب جميع الاشتراكات النشطة
                var today = DateTime.Now.Date;
                var activeSubs = await _context.UserSubscriptions
                    .Where(s => s.UserId == userId &&
                                s.IsActive &&
                                s.PaymentStatus == "paid" &&
                                s.StartDate <= today &&
                                s.EndDate >= today)
                    .Select(s => s.Id)
                    .ToListAsync();

                if (!activeSubs.Any())
                    return;

                // 🧩 اجلب الميزة الخاصة بإنشاء المجموعات
                var feature = await _context.PackageFeatures
                    .Where(f => f.forCreatingGroups && f.PlatformId == 1)
                    .Select(f => f.Id)
                    .FirstOrDefaultAsync();

                if (feature == 0)
                    return;

                // 🧩 اجلب أول usage نشط فيه باقي رصيد
                var usage = await _context.subscription_usage
                    .Where(u => activeSubs.Contains(u.SubscriptionId) &&
                                u.FeatureId == feature &&
                                u.UsedCount < u.LimitCount)
                    .OrderBy(u => u.UsedCount)
                    .FirstOrDefaultAsync();

                if (usage != null)
                {
                    usage.UsedCount += count;
                    _context.subscription_usage.Update(usage);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
            }
        }



    }
}
