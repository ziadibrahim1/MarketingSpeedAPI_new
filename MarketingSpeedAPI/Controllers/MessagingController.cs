using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Asn1.Crmf;
using RestSharp;
using System.Text.Json;
using TL;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MessagingController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly RestClient _client;
        private readonly string _apiKey;

        public MessagingController(AppDbContext context, IOptions<WasenderSettings> wasenderOptions)
        {
            _context = context;

            _apiKey = wasenderOptions.Value.ApiKey;
            _client = new RestClient(wasenderOptions.Value.BaseUrl.TrimEnd('/'));
        }

        // ===== إرسال رسالة =====
        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequests req)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)req.UserId && a.PlatformId == req.PlatformId);

            if (account == null || account.WasenderSessionId == null)
                return NotFound(new { success = false, message = "No active WhatsApp session" });

            var message = new Models.Message
            {
                PlatformId = req.PlatformId,
                UserId = req.UserId,
                Title = req.Title ?? string.Empty,
                Body = req.Message,
                Targets = JsonSerializer.Serialize(req.Recipients),
                Suggestions = req.Suggestions,
                Attachments = req.Attachments,
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            };

            _context.Add(message);
            await _context.SaveChangesAsync();

            // إرسال الرسائل عبر RestSharp
            var request = new RestRequest($"/api/messages/send/{account.WasenderSessionId}", Method.Post);
            request.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            request.AddJsonBody(new
            {
                type = req.Type,
                recipients = req.Recipients,
                message = req.Message
            });

            var resp = await _client.ExecuteAsync(request);
            var success = resp.IsSuccessful;

            message.Status = success ? "sent" : "failed";
            message.SentAt = DateTime.UtcNow;

            foreach (var r in req.Recipients)
            {
                message.Logs.Add(new MessageLog
                {
                    MessageId = message.Id,
                    Recipient = r,
                    PlatformId = req.PlatformId,
                    Status = success ? "sent" : "failed",
                    ErrorMessage = success ? null : resp.Content,
                    AttemptedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            return Ok(new { success });
        }

        // ===== جلب المجموعات مع الأعضاء =====
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

            // 1) هات المجموعات
            var groupsRequest = new RestRequest($"/api/groups", Method.Get);
            groupsRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            var groupsResp = await _client.ExecuteAsync(groupsRequest);

            if (!groupsResp.IsSuccessful)
                return StatusCode((int)groupsResp.StatusCode, groupsResp.Content);

            var result = new List<object>();

            var groupsJson = JsonDocument.Parse(groupsResp.Content);
            if (groupsJson.RootElement.TryGetProperty("data", out var groups))
            {
                // نجهز التاسكات لكل الجروبات
                var tasks = groups.EnumerateArray().Select(async g =>
                {
                    var groupName = g.GetProperty("name").GetString() ?? "Unnamed Group";
                    var groupId = g.GetProperty("id").GetString();

                    int memberCount = 0;

                    try
                    {
                        // 2) API metadata لجلب الأعضاء
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
                    catch { /* تجاهل أي خطأ في جروب */ }

                    return new
                    {
                        id = groupId,
                        name = groupName,
                        membersCount = memberCount
                    };
                });

                // نشغل كل التاسكات مع بعض
                result = (await Task.WhenAll(tasks)).ToList<object>();
            }

            return Ok(result);
        }
        // ===== جلب أعضاء مجموعة محددة حسب JID =====
        [HttpGet("group-members/{userId}/{platformId}/{groupJid}")]
        public async Task<IActionResult> GetGroupMembersByGroupJid(ulong userId, int platformId, string groupJid)
        {
            // جلب حساب الواتساب للمستخدم
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == platformId);

            if (account == null || account.WasenderSessionId == null)
                return NotFound(new { success = false, message = "No active WhatsApp session" });

            // إنشاء طلب RestSharp لجلب أعضاء المجموعة
            var request = new RestRequest($"/api/groups/{account.WasenderSessionId}/{groupJid}/members", Method.Get);
            request.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            var resp = await _client.ExecuteAsync(request);

            if (!resp.IsSuccessful)
                return StatusCode((int)resp.StatusCode, resp.Content);

            // تحويل JSON إلى قائمة من الأعضاء (أرقام فقط بدون @s.whatsapp.net)
            var membersList = new List<string>();
            var membersJson = JsonDocument.Parse(resp.Content);
            if (membersJson.RootElement.TryGetProperty("data", out var members))
            {
                foreach (var m in members.EnumerateArray())
                {
                    var memberStr = m.GetString() ?? string.Empty;
                    // استخراج الرقم فقط قبل '@'
                    var numberOnly = memberStr.Split('@')[0];
                    membersList.Add(numberOnly);
                }
            }

            return Ok(new { success = true, data = membersList });
        }

        // ===== جلب دردشات المستخدم =====
        [HttpGet("chats/{userId}/{platformId}")]
        public async Task<IActionResult> GetChats(ulong userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == platformId);

            if (account == null || account.WasenderSessionId == null)
                return NotFound();

            var request = new RestRequest($"/api/chats/{account.WasenderSessionId}", Method.Get);
            request.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            var resp = await _client.ExecuteAsync(request);

            if (!resp.IsSuccessful)
                return StatusCode((int)resp.StatusCode, resp.Content);

            return Ok(resp.Content);
        }
    }
}
