﻿using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
namespace MarketingSpeedAPI.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    public class sentMessagesController : ControllerBase
    {
        private readonly string _apiKey;
        private readonly RestClient _client;
        private readonly AppDbContext _context;

        public sentMessagesController(AppDbContext context, IOptions<WasenderSettings> wasenderOptions)
        {
            _context = context;
            _client = new RestClient(wasenderOptions.Value.BaseUrl.TrimEnd('/'));
            _apiKey = wasenderOptions.Value.ApiKey;
        }


        [HttpGet]
        public async Task<IActionResult> GetMessages()
        {
            var messages = await _context.Messages
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();

            return Ok(messages);
        }

        // GET: api/messages/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetMessage(int id)
        {
            var msg = await _context.Messages.FindAsync(id);
            if (msg == null) return NotFound();
            return Ok(msg);
        }

        // DELETE: api/messages/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMessage(int id)
        {
            var msg = await _context.Messages.FindAsync(id);
            if (msg == null) return NotFound();

            _context.Messages.Remove(msg);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("resend/{id}")]
        public async Task<IActionResult> ResendMessage(int id)
        {
            var msg = await _context.Messages.FindAsync(id);
            if (msg == null)
                return NotFound(new { success = false, message = "Message not found" });

            var today = DateTime.UtcNow.Date;

            var subscription = await _context.UserSubscriptions
                .Where(s => s.UserId == msg.UserId &&
                            s.IsActive &&
                            s.PaymentStatus == "paid" &&
                            s.StartDate <= today &&
                            s.EndDate >= today)
                .OrderByDescending(s => s.EndDate)
                .FirstOrDefaultAsync();

            if (subscription == null)
                return Ok(new { success = false, status = "0", message = "Subscription invalid" });

            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == msg.UserId && a.PlatformId == 1 && a.Status == "connected");

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

                var log = new MessageLog
                {
                    MessageId = msg.Id,
                    Recipient = groupId,
                    PlatformId = account.PlatformId,
                    Status = response.IsSuccessful ? "sent" : "failed",
                    ErrorMessage = response.IsSuccessful ? null : response.Content,
                    AttemptedAt = DateTime.UtcNow,
                    ExternalMessageId = externalId
                };
                _context.message_logs.Add(log);

                if (response.IsSuccessful)
                {
                    msg.Status = "sent";
                    msg.SentAt = DateTime.UtcNow;
                    results.Add(new { groupId, fileUrl, success = true, externalId });
                }
                else
                {
                    results.Add(new { groupId, fileUrl, success = false, error = response.Content });
                }

                await _context.SaveChangesAsync();
            }

            var previousRecipients = await _context.message_logs
                .Where(l => l.MessageId == msg.Id)
                .Select(l => l.Recipient)
                .Distinct()
                .ToListAsync();

            var imageUrls = string.IsNullOrEmpty(msg.Attachments)
                ? new List<string>()
                : JsonConvert.DeserializeObject<List<string>>(msg.Attachments) ?? new();

            foreach (var groupId in previousRecipients)
            {
                if (imageUrls.Any())
                {
                    for (int i = 0; i < imageUrls.Count; i++)
                    {
                        bool isLast = i == imageUrls.Count - 1;
                        var msgTypeStr = GetMessageTypeFromExtension(imageUrls[i]);

                        var body = new Dictionary<string, object?>
                {
                    { "to", groupId },
                    { msgTypeStr, imageUrls[i] }
                };

                        if (isLast && !string.IsNullOrWhiteSpace(msg.Body))
                            body.Add("text", AddRandomDots(msg.Body));

                        await SendAndLogAsync(groupId, body, imageUrls[i]);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(msg.Body))
                {
                    var body = new Dictionary<string, object?>
            {
                { "to", groupId },
                { "text", AddRandomDots(msg.Body) }
            };
                    await SendAndLogAsync(groupId, body);
                }

                if (previousRecipients.Count > 1 && groupId != previousRecipients.Last())
                {
                    int delay = random.Next(5000, 7001);
                    await Task.Delay(delay);
                }
            }

            if (msg.Status == "pending")
                msg.Status = "failed";

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                status = "2",
                message = msg.Body,
                messageId = msg.Id,
                results
            });
        }

        [HttpGet("user/{userId}/whatsapp-stats")]
        public async Task<IActionResult> GetUserWhatsappStats(long userId)
        {
            var since = DateTime.UtcNow.AddHours(-24);

            var query = from m in _context.Messages
                        join l in _context.message_logs on m.Id equals l.MessageId
                        where m.UserId == userId && m.PlatformId == 1  
                              && l.Status == "sent"
                              && l.AttemptedAt >= since
                        select new { l.Recipient };

            var logs = await query.ToListAsync();

            var numbersCount = logs.Count(l => !l.Recipient.Contains("@g.us"));

            var groupsCount = logs.Count(l => l.Recipient.Contains("@g.us"));

            return Ok(new
            {
                numbersCount,
                groupsCount
            });
        }


        [HttpGet("user/{userId}/messages-stats")]
        public async Task<IActionResult> GetUserMessagesStats(long userId, string range = "day")
        {
            var now = DateTime.UtcNow.Date;

            var logs = await (from m in _context.Messages
                              join l in _context.message_logs on m.Id equals l.MessageId
                              where m.UserId == userId && l.Status == "sent"
                              select l.AttemptedAt)
                             .ToListAsync();

            if (range == "day")
            {
                
                var last7Days = Enumerable.Range(0, 7).Select(i => now.AddDays(-6 + i)).ToList();

                var grouped = logs
                    .Where(d => d >= last7Days.First())
                    .GroupBy(d => d.Date)
                    .ToDictionary(g => g.Key, g => g.Count());

                var result = last7Days
                    .Select(d => new {
                        Period = d.ToString("yyyy-MM-dd"),  
                        Count = grouped.ContainsKey(d) ? grouped[d] : 0
                    })
                    .ToList();
                return Ok(result);
            }
            else if (range == "week")
            {
                var calendar = System.Globalization.CultureInfo.InvariantCulture.Calendar;
                var weekRule = System.Globalization.CalendarWeekRule.FirstFourDayWeek;
                var dayOfWeek = DayOfWeek.Saturday;

                var now1 = DateTime.UtcNow.Date;

                var startOfMonth = new DateTime(now1.Year, now1.Month, 1);

                var grouped = logs
                    .Where(d => d.Date >= startOfMonth && d.Date <= now1)
                    .GroupBy(d => calendar.GetWeekOfYear(d, weekRule, dayOfWeek))
                    .ToDictionary(g => g.Key, g => g.Count());

                var result = new List<object>();

                var currentWeek = calendar.GetWeekOfYear(now1, weekRule, dayOfWeek);

                var firstWeekOfMonth = calendar.GetWeekOfYear(startOfMonth, weekRule, dayOfWeek);

                var totalWeeksInMonth = currentWeek - firstWeekOfMonth + 1;

                for (int i = 0; i < totalWeeksInMonth; i++)
                {
                    var weekNumber = firstWeekOfMonth + i;
                    var periodLabel = $"الأسبوع {i + 1}";
                    var count = grouped.ContainsKey(weekNumber) ? grouped[weekNumber] : 0;

                    result.Add(new
                    {
                        Period = periodLabel,
                        Count = count
                    });
                }

                return Ok(result);
            }
            else // month
            {
                var months = Enumerable.Range(1, 12).ToList(); // 01..12
                var grouped = logs
                    .Where(d => d.Year == now.Year)
                    .GroupBy(d => d.Month)
                    .ToDictionary(g => g.Key, g => g.Count());

                var result = months
                    .Select(m => new {
                        Period = m.ToString("00"), // 01..12
                        Count = grouped.ContainsKey(m) ? grouped[m] : 0
                    })
                    .ToList();

                return Ok(result);
            }
        }



    }
}
