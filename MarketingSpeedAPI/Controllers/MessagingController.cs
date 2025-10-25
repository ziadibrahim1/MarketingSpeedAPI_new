using JamesWright.SimpleHttp;
using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

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
        public MessagingController(AppDbContext context, IOptions<WasenderSettings> wasenderOptions, IServiceProvider serviceProvider)
        {
            _context = context;
            _apiKey = wasenderOptions.Value.ApiKey;
            _client = new RestClient(wasenderOptions.Value.BaseUrl.TrimEnd('/'));
            _serviceProvider = serviceProvider;
        }



        [HttpPost("send-to-groups/{userId}")]
        public async Task<IActionResult> SendToGroups(ulong userId, [FromBody] SendGroupsRequest req)
        {
           
            var today = DateTime.UtcNow.Date;
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
                Title = $"Send to groups {DateTime.UtcNow:yyyyMMddHHmmss}",
                Body = req.Message ?? "", 
                Targets = JsonConvert.SerializeObject(groupsToSend),
                Attachments = (req.ImageUrls == null || !req.ImageUrls.Any()) ? null : JsonConvert.SerializeObject(req.ImageUrls),
                Status = "pending",
                CreatedAt = DateTime.UtcNow
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
            newMessage.SentAt = DateTime.UtcNow;

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
                AttemptedAt = DateTime.UtcNow,
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


        [HttpGet("check-packege-account/{userId}")]

        public async Task<IActionResult> CheckPackegeAccount(ulong userId)
        {
            var today = DateTime.UtcNow.Date;

            var subscriptions = await _context.UserSubscriptions
                .Where(s => s.UserId == (int)userId &&
                            s.IsActive &&
                            s.PaymentStatus == "paid" &&
                            s.StartDate <= today &&
                            s.EndDate >= today)
                .OrderByDescending(s => s.EndDate)
                .AsNoTracking()
                .ToListAsync();

            if (subscriptions == null || subscriptions.Count == 0)
                return Ok(new { success = false, status = "0", message = "No active subscription" });

            var account = await _context.user_accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1);
            if (account == null || string.IsNullOrEmpty(account.WasenderSessionId?.ToString()))
                return Ok(new { success = false, status = "1", message = "No account found" });

            if (account.Status != "connected")
            {
                var deleted1 = await DeleteWasenderSession(account);
                if (deleted1)
                {
                    _context.user_accounts.Remove(account);
                    await _context.SaveChangesAsync();
                    return Ok(new { success = false, status = "1", message = "No account found" });
                }
                return Ok(new { success = false, status = "1", message = "No account found" });
            }

            var packageIds = subscriptions.Select(s => s.PackageId).Distinct().ToList();

            var features = await _context.PackageFeatures
                .Where(f => packageIds.Contains(f.PackageId) && f.PlatformId == 1)
                .AsNoTracking()
                .ToListAsync();

            var subscriptionIds = subscriptions.Select(s => s.Id).ToList();
            var usages = await _context.subscription_usage
                .Where(u => u.UserId == (int)userId && subscriptionIds.Contains(u.SubscriptionId))
                .GroupBy(u => new { u.SubscriptionId, u.featureId })
                .Select(g => new
                {
                    g.Key.SubscriptionId,
                    g.Key.featureId,
                    TotalMessage = g.Sum(x => x.MessageCount),
                    TotalMedia = g.Sum(x => x.MediaCount)
                })
                .ToListAsync();

            var allFeatures = from sub in subscriptions
                              join f in features on sub.PackageId equals f.PackageId
                              join u in usages on new { sub.Id, featureId = f.Id } equals new { Id = u.SubscriptionId, u.featureId } into usageGroup
                              from usage in usageGroup.DefaultIfEmpty()
                              select new
                              {
                                  f.Id,
                                  f.feature,
                                  f.forMembers,
                                  f.forCreatingGroups,
                                  f.LimitCount,
                                  f.sendingLimit,
                                  SubscriptionId = sub.Id,
                                  PackageId = sub.PackageId,
                                  CurrentMessageUsage = f.LimitCount - (usage?.TotalMessage ?? 0),
                                  CurrentSendingUsage = usage?.TotalMedia ?? 0,
                                  IsMessageLimitExceeded = (usage?.TotalMessage ?? 0) > f.LimitCount,
                                  IsMediaLimitExceeded = (usage?.TotalMedia ?? 0) > f.sendingLimit
                              };

            return Ok(new
            {
                success = true,
                status = "1",
                message = "Ok",
                features = allFeatures
            });
        }





        private async Task<bool> DeleteWasenderSession(UserAccount account)
        {
            try
            {
                var client = new RestClient($"https://www.wasenderapi.com/api/whatsapp-sessions/{account.WasenderSessionId}");
                var request = new RestRequest("", Method.Delete);  
                request.AddHeader("Authorization", $"Bearer {_apiKey}");

                var response = await client.ExecuteAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(response.Content) &&
                    response.Content.Contains("noaccount", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }


                return false;
            }
            catch (Exception ex)
            {
                return false;
            }
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

            if (account == null || account.WasenderSessionId == null)
                return NotFound();

            var leftJids = await _context.LeftGroups
                .Where(lg => lg.UserId == (int)userId)
                .Select(lg => lg.GroupId)
                .ToListAsync();

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
                        name = g.GetProperty("name").GetString()
                    }).GroupBy(g => g.id) 
    .Select(g => g.First())
    .ToList<object>()
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

            if (account == null || account.WasenderSessionId == null)
                return NotFound(new { success = false, message = "No active WhatsApp session" });

            var request = new RestRequest($"/api/groups/{groupJid}/participants", Method.Get);
            request.AddHeader("Authorization", $"Bearer {account.AccessToken}");

            async Task<RestResponse> ExecuteWithRetryAsync()
            {
                var resp = await _client.ExecuteAsync(request);

                if ((int)resp.StatusCode == 429)  
                {
                    try
                    {
                        using var json = JsonDocument.Parse(resp.Content);
                        if (json.RootElement.TryGetProperty("retry_after", out var retryProp))
                        {
                            int retryAfter = retryProp.GetInt32();
                            await Task.Delay(retryAfter * 1000);  
                            resp = await _client.ExecuteAsync(request); 
                        }
                    }
                    catch
                    {
                        using var json = JsonDocument.Parse(resp.Content);
                        if (json.RootElement.TryGetProperty("retry_after", out var retryProp))
                        {
                            int retryAfter = retryProp.GetInt32();
                            await Task.Delay(retryAfter * 1000);  
                            resp = await _client.ExecuteAsync(request);  
                        }
                    }
                }

                return resp;
            }

            var respFinal = await ExecuteWithRetryAsync();

            if (!respFinal.IsSuccessful)
                return StatusCode((int)respFinal.StatusCode, respFinal.Content);

            var membersList = new List<object>();
            var membersJson = JsonDocument.Parse(respFinal.Content);

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

                   
                    var numberOnly = jid?.Split('@')[0];
                    if (!string.IsNullOrWhiteSpace(numberOnly))
                    {
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
                Attachments = (req.ImageUrls == null || req.ImageUrls.Count == 0) ? null : JsonConvert.SerializeObject(req.ImageUrls),
                Status = "pending",
                CreatedAt = DateTime.UtcNow
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

        [HttpPost("send-to-single-group/{userId}")]
        public async Task<IActionResult> SendToSingleGroup(ulong userId, [FromBody] SendSingleGroupRequest req)
        {
            if (string.IsNullOrEmpty(req.GroupId))
            {
                return BadRequest(new { success = false, blocked = false, error = "GroupId is required" });
            }

            var account = await _context.user_accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null)
            {
                return Ok(new { success = false, blocked = false, error = "No connected account found" });
            }

            string? finalText = null;
            if (!string.IsNullOrWhiteSpace(req.Message))
            {
                finalText = SpinText(req.Message);
            }

            var body = new Dictionary<string, object?> { { "to", req.GroupId } };
            bool hasAttachment = req.ImageUrls != null && req.ImageUrls.Any();

            if (hasAttachment)
            {
                var mediaUrl = req.ImageUrls.First();
                body[GetMessageTypeFromExtension(mediaUrl)] = mediaUrl;
                if (finalText != null)
                {
                    body["text"] = finalText;
                }
            }
            else if (finalText != null)
            {
                body["text"] = finalText;
            }

            if (body.Count <= 1)
            {
                return Ok(new { success = false, blocked = false, error = "Message body and attachments are empty" });
            }

            var request = new RestRequest("/api/send-message", Method.Post);
            request.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            request.AddHeader("Content-Type", "application/json");
            request.AddJsonBody(body);

            var response = await _client.ExecuteAsync(request);

            bool success = response.IsSuccessful;
            string errorMessage = success ? null : (response.ErrorMessage ?? response.Content);

            bool isBlocked = !success && (
                errorMessage?.Contains("group not found", StringComparison.OrdinalIgnoreCase) == true ||
                errorMessage?.Contains("not a participant", StringComparison.OrdinalIgnoreCase) == true ||
                errorMessage?.Contains("forbidden", StringComparison.OrdinalIgnoreCase) == true
            );

            _ = Task.Run(async () =>
            {
                try
                {
                    string? externalId = null;
                    if (success && !string.IsNullOrEmpty(response.Content))
                    {
                        try { externalId = JObject.Parse(response.Content)["data"]?["msgId"]?.ToString(); } catch { }
                    }

                    using var scope = _serviceProvider.CreateScope();
                    var scopedContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var log = new MessageLog
                    {
                        MessageId = (int)(req.MainMessageId ?? 0), 
                        Recipient = req.GroupId,
                        PlatformId = account.PlatformId,
                        Status = success ? "sent" : "failed",
                        ErrorMessage = errorMessage,
                        AttemptedAt = DateTime.UtcNow,
                        ExternalMessageId = externalId
                    };

                    scopedContext.message_logs.Add(log);

                    if (isBlocked)
                    {
                        if (!await scopedContext.BlockedGroups.AnyAsync(bg => bg.GroupId == req.GroupId && bg.UserId == (int)userId))
                        {
                            scopedContext.BlockedGroups.Add(new BlockedGroup { GroupId = req.GroupId, UserId = (int)userId, CreatedAt = DateTime.UtcNow });
                        }
                    }
                    await scopedContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in background logging for SendToSingleGroup");
                }
            });

            return Ok(new { success, blocked = isBlocked });
        }
        string NormalizePhone(string number)
        {
            if (string.IsNullOrEmpty(number)) return number;
            return new string(number.Where(char.IsDigit).ToArray());
        }

        [HttpPost("send-to-single-member/{userId}")]
        public async Task<IActionResult> SendToSingleMember(ulong userId, [FromBody] SendSingleMemberRequest req)
        {
            if (string.IsNullOrEmpty(req.Recipient))
                return BadRequest(new { success = false, blocked = false, error = "Recipient is required" });

            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId &&
                                          a.PlatformId == req.PlatformId &&
                                          a.Status == "connected");

            if (account == null || string.IsNullOrEmpty(account.WasenderSessionId?.ToString()))
                return Ok(new { success = false, blocked = false, error = "No connected account found" });

            if (NormalizePhone(account.AccountIdentifier) == NormalizePhone(req.Recipient))
                return Ok(new { success = false, blocked = false, error = "Recipient number matches sender" });

            // ✅ دالة توليد اسم واقعي + عشوائي بسيط
            string GenerateRealisticName()
            {
                string[] firstNames = { "Ahmed", "Mohamed", "Ali", "Omar", "Sara", "Mona", "Youssef", "Khaled", "Hassan", "Nour", "Tamer", "Rania", "Lina", "Adel", "Sami", "Ehab", "Walid", "Karim", "Reem", "Fadi" };
                string[] lastNames = { "Saeed", "Hassan", "Mahmoud", "Ibrahim", "Ali", "Mostafa", "Salem", "Kamel", "Fouad", "Tawfik", "Nasr", "Othman", "Mansour", "Zaki", "Hegazy", "Ramadan", "Khalifa", "Farouk", "Saber", "Ashraf" };

                string first = firstNames[Random.Shared.Next(firstNames.Length)];
                string last = lastNames[Random.Shared.Next(lastNames.Length)];

                // إضافة 2 أو 3 حروف عشوائية عشان الاسم يكون فريد فعلًا
                string randomSuffix = new string(Enumerable.Repeat("abcdefghijklmnopqrstuvwxyz", 3)
                    .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());

                return $"{first} {last} {randomSuffix}";
            }

            // ✅ إضافة الرقم إلى جهات الاتصال قبل الإرسال
            try
            {
                string uniqueName = GenerateRealisticName(); // مثال: "Ahmed Saeed qzt"

                var contactRequest = new RestRequest("/api/contacts", Method.Put);
                contactRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
                contactRequest.AddHeader("Content-Type", "application/json");

                var contactBody = new
                {
                    jid = $"{NormalizePhone(req.Recipient)}@s.whatsapp.net",
                    fullName = uniqueName,
                    saveOnPrimaryAddressbook = true
                };

                contactRequest.AddJsonBody(contactBody);
                var contactResponse = await _client.ExecuteAsync(contactRequest);

                if (!contactResponse.IsSuccessful)
                {
                   
                }
                
            }
            catch (Exception ex)
            {
                 
            }
            await Task.Delay(3000);
            // ✅ تجهيز جسم الرسالة
            var body = new Dictionary<string, object?> { { "to", req.Recipient } };

            if (req.ImageUrls != null && req.ImageUrls.Any())
            {
                var mediaUrl = req.ImageUrls.First();
                body[GetMessageTypeFromExtension(mediaUrl)] = mediaUrl;
                if (req.Message != null) body["text"] = req.Message;
            }
            else if (req.Message != null)
            {
                body["text"] = req.Message;
            }

            if (body.Count <= 1)
                return Ok(new { success = false, blocked = false, error = "Message body and attachments are empty" });
            // ✅ إرسال حالة "يكتب الآن" قبل الإرسال
            if (!string.IsNullOrEmpty(req.Message))
            {
                try
                {
                    // حساب الوقت التقديري بناء على طول الرسالة
                    int messageLength = req.Message.Length;

                    // الوقت التقديري لكل حرف (مثلاً 70ms لكل حرف، وحد أقصى 6 ثوانٍ)
                    int delayMs = Math.Min(messageLength * 70, 1000);

                    // تجهيز طلب presence update
                    var typingRequest = new RestRequest("/api/send-presence-update", Method.Post);
                    typingRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
                    typingRequest.AddHeader("Content-Type", "application/json");

                    var presenceBody = new
                    {
                        jid = $"{NormalizePhone(req.Recipient)}@s.whatsapp.net",
                        type = "composing",
                        delayMs = delayMs  
                    };

                    typingRequest.AddJsonBody(presenceBody);
                    var typingResponse = await _client.ExecuteAsync(typingRequest);

                    if (typingResponse.IsSuccessful)
                    {
                         
                    }
                    else
                    {
                         
                    }

                    // الانتظار نفس مدة الكتابة التقديرية قبل الإرسال الفعلي
                    await Task.Delay(delayMs);
                }
                catch (Exception ex)
                {
                     
                }
            }

            // ✅ إرسال الرسالة
            var request = new RestRequest("/api/send-message", Method.Post);
            request.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            request.AddHeader("Content-Type", "application/json");
            request.AddJsonBody(body);

            bool success = false;
            bool isBlocked = false;
            string? errorMessage = null;
            string? externalId = null;

            const int maxRetries = 3;
            int attempt = 0;

            while (attempt < maxRetries)
            {
                attempt++;
                var response = await _client.ExecuteAsync(request);

                if ((int)response.StatusCode == 429)
                {
                    int waitSec = Random.Shared.Next(15, 30);
                    await Task.Delay(waitSec * 1000);
                    continue;
                }

                success = response.IsSuccessful;
                errorMessage = success ? null : (response.ErrorMessage ?? response.Content);

                if (success)
                {
                    try
                    {
                        externalId = JObject.Parse(response.Content)["data"]?["msgId"]?.ToString();
                    }
                    catch { }
                }

                isBlocked = !success && (
                    errorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ||
                    errorMessage?.Contains("not a participant", StringComparison.OrdinalIgnoreCase) == true ||
                    errorMessage?.Contains("forbidden", StringComparison.OrdinalIgnoreCase) == true ||
                    errorMessage?.Contains("blocked", StringComparison.OrdinalIgnoreCase) == true
                );

                if (!success && attempt < maxRetries)
                {
                    await Task.Delay(Random.Shared.Next(5, 10) * 2000);
                    continue;
                }

                break;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var scopedContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var log = new MessageLog
                    {
                        MessageId = (int)(req.MainMessageId ?? 0),
                        Recipient = req.Recipient,
                        PlatformId = account.PlatformId,
                        Status = success ? "sent" : "failed",
                        ErrorMessage = errorMessage,
                        AttemptedAt = DateTime.UtcNow,
                        ExternalMessageId = externalId
                    };

                    scopedContext.message_logs.Add(log);

                    if (isBlocked)
                    {
                        if (!await scopedContext.BlockedGroups
                            .AnyAsync(bg => bg.GroupId == req.Recipient && bg.UserId == (int)userId))
                        {
                            scopedContext.BlockedGroups.Add(new BlockedGroup
                            {
                                GroupId = req.Recipient,
                                UserId = (int)userId,
                                CreatedAt = DateTime.UtcNow
                            });
                        }
                    }

                    await scopedContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                     
                }
            });

            return Ok(new { success, blocked = isBlocked, error = errorMessage });
        }

        private string AddSmartVariation(string message)
        {
            var symbols = new[] { ".", "..", "!", "!!", "🤝", "✨", "🙂", "~", "،", "..." };
            var connectors = new[] { "", " ", " - ", " ~ ", " » ", " : ", " | " };
            var randomSymbol = symbols[Random.Shared.Next(symbols.Length)];
            var connector = connectors[Random.Shared.Next(connectors.Length)];
            var prefix = Random.Shared.NextDouble() < 0.3 ? randomSymbol + connector : "";
            var suffix = Random.Shared.NextDouble() < 0.6 ? connector + randomSymbol : "";
            return $"{prefix}{message}{suffix}";
        }


        // ---------- دوال مساعدة جديدة/مساعدة ----------

        private int? ParseRetryAfterSeconds(string? content)
        {
            if (string.IsNullOrEmpty(content)) return null;
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("retry_after", out var retryProp) && retryProp.ValueKind == JsonValueKind.Number)
                {
                    return retryProp.GetInt32();
                }
                // أحياناً تكون داخل data أو meta
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                    data.TryGetProperty("retry_after", out var retry2) && retry2.ValueKind == JsonValueKind.Number)
                {
                    return retry2.GetInt32();
                }
            }
            catch { }
            return null;
        }

        private int? ParseRetryAfterHeaderSeconds(IList<Parameter> headers)
        {
            if (headers == null) return null;
            try
            {
                // RestResponse.Headers is a collection of Parameter in RestSharp v107+, قد تختلف حسب النسخة
                var header = headers.FirstOrDefault(h => string.Equals(h.Name, "Retry-After", StringComparison.OrdinalIgnoreCase));
                if (header != null && header.Value != null)
                {
                    if (int.TryParse(header.Value.ToString(), out int seconds))
                        return seconds;
                }
            }
            catch { }
            return null;
        }


        private static readonly Random _rand = new Random();


        private string AddVariations(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return message;

            var variations = new List<string>
    {
                    
        $"{message},",              
        $"{message}:",                
        InsertAtNearestSpace(message, "·"),
         $",{message}",
        InsertAtNearestSpace(message, "."),
        $" ~ {message}",
        InsertAtNearestSpace(message, ","),
        $"{message}ّ ",
        InsertAtNearestSpace(message, "’") ,
         $". {message}"
    };

            return variations.OrderBy(x => _rand.Next()).First();
        }

       
        private string InsertAtNearestSpace(string message, string symbol)
        {
            int mid = message.Length / 2;

            int spaceBefore = message.LastIndexOf(' ', mid);
            int spaceAfter = message.IndexOf(' ', mid);

            int insertPos;
            if (spaceBefore == -1 && spaceAfter == -1)
            {
                insertPos = message.Length;
            }
            else if (spaceBefore == -1)
            {
                insertPos = spaceAfter;
            }
            else if (spaceAfter == -1)
            {
                insertPos = spaceBefore;
            }
            else
            {
                insertPos = (mid - spaceBefore <= spaceAfter - mid) ? spaceBefore : spaceAfter;
            }

            return message.Substring(0, insertPos) + " " + symbol + " " + message.Substring(insertPos).TrimStart();
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

            // تحقق إذا كانت المجموعة موجودة
            var existingSubscription = await _context.group_subscriptions
                .FirstOrDefaultAsync(g => g.UserId == userId && g.GroupId == req.Name && g.Status == "active");

            string groupJid = "";
            if (existingSubscription != null)
            {
                groupJid = existingSubscription.GroupId;
            }
            else
            {
                // إنشاء المجموعة
                var createRequest = new RestRequest("/api/groups", Method.Post);
                createRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
                createRequest.AddHeader("Content-Type", "application/json");

                var initialMembers = req.Members?.Take(2).ToArray() ?? Array.Empty<string>();
                createRequest.AddJsonBody(new { name = req.Name, participants = initialMembers });

                var createResponse = await _client.ExecuteAsync(createRequest);
                if (!createResponse.IsSuccessful)
                    return Ok(new { success = false, message = "Failed to create group", error = createResponse.Content });

                var json = JsonDocument.Parse(createResponse.Content);
                var data = json.RootElement.GetProperty("data");
                groupJid = data.GetProperty("id").GetString();

                // حفظ في قاعدة البيانات
                var sub = new GroupSubscription
                {
                    UserId = (int)userId,
                    GroupId = groupJid,
                    StartDate = DateTime.UtcNow,
                    EndDate = DateTime.UtcNow.AddMonths(1),
                    Status = "active",
                    LastBatchTime = DateTime.UtcNow
                };
                _context.group_subscriptions.Add(sub);
                await _context.SaveChangesAsync();
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
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, message = "No connected account found" });

            if (string.IsNullOrWhiteSpace(req.GroupId) || req.Members == null || req.Members.Count == 0)
                return Ok(new { success = false, message = "Invalid request" });

            var results = new List<object>();

            foreach (var member in req.Members)
            {
                try
                {
                    var addRequest = new RestRequest($"/api/groups/{req.GroupId}/participants/add", Method.Post);
                    addRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
                    addRequest.AddHeader("Content-Type", "application/json");
                    addRequest.AddJsonBody(new { participants = new List<string> { member } });

                    var response = await _client.ExecuteAsync(addRequest);

                    bool success = response.IsSuccessful;
                    string? error = success ? null : response.Content;

                    results.Add(new
                    {
                        member,
                        success,
                        error
                    });

                    if (!success)
                    {
                        _logger.LogWarning("Failed to add member {Member}: {Error}", member, response.Content);
                    }

                    // ⏳ انتظار عشوائي بين 10 و 20 ثانية قبل إضافة العضو التالي
                    int delaySec = Random.Shared.Next(10, 21);
                    _logger.LogInformation("Waiting {Delay} seconds before adding next member...", delaySec);
                    await Task.Delay(delaySec * 1000);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while adding member {Member}", member);
                    results.Add(new { member, success = false, error = ex.Message });
                }
            }

            // ✅ تحديث وقت آخر Batch بعد الانتهاء
            try
            {
                var sub = await _context.group_subscriptions.FirstOrDefaultAsync(g => g.GroupId == req.GroupId);
                if (sub != null)
                {
                    sub.LastBatchTime = DateTime.UtcNow;
                    _context.group_subscriptions.Update(sub);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while updating LastBatchTime for group {GroupId}", req.GroupId);
            }

            return Ok(new
            {
                success = true,
                message = "Members processed (added one by one)",
                results
            });
        }


    }
}
