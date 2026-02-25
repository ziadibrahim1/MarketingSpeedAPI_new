using MarketingSpeedAPI.Data;
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


        [HttpGet("sent/{userid}")]
        public async Task<IActionResult> GetMessagesByBody(int userid)
        {
            var logs = await _context.message_logs
                .Where(l => l.Status == "sent"  && l.UserId == userid)
                .Select(l => new
                {
                    Body = l.body ?? "",                // 👈 هنا بنمنع NULL يوصل للـ reader
                    AttemptedAt = l.AttemptedAt
                })
                .AsNoTracking()
                .ToListAsync();

            if (logs.Count == 0)
                return Content("[]", "application/json");

            var grouped = logs
                .GroupBy(l => l.Body.Trim())
                .Select(g => new
                {
                    body = g.Key,
                    count = g.Count(),
                    lastSent = g.Max(x => x.AttemptedAt)
                })
                .OrderByDescending(g => g.lastSent)
                .ToList();

            var json = System.Text.Json.JsonSerializer.Serialize(grouped,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

            return Content(json, "application/json");
        }

        // GET: api/messages/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetMessage(int id)
        {
            var msg = await _context.Messages.FindAsync(id);
            if (msg == null) return NotFound();
            return Ok(msg);
        }

        // DELETE: api/sentMessages/{body}
        [HttpPost("delete-messages")]
        public async Task<IActionResult> DeleteMessages([FromBody] DeleteMessagesRequest request)
        {
            if (request.Bodies == null || !request.Bodies.Any())
                return BadRequest(new { message = "Bodies list is empty" });

            var normalizedBodies = request.Bodies
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .Select(b => b.Trim())
                .ToList();

            var affectedRows = await _context.message_logs
                .Where(l =>
                    l.Status != "deleted" &&
                    l.body != null &&
                    normalizedBodies.Contains(l.body.Trim())
                )
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(l => l.Status, "deleted")
                );

            if (affectedRows == 0)
                return NotFound(new { message = "No messages found" });

            return Ok(new
            {
                message = $"Deleted {affectedRows} messages",
                count = affectedRows
            });
        }

        [HttpGet("user/{userId}/whatsapp-stats")]
        public async Task<IActionResult> GetUserWhatsappStats(long userId)
        {
           
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.Now, tz);
            var startOfDay = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, 0, 0, 0);
            var endOfDay = startOfDay.AddDays(1);

            // 🔹 نحولهم إلى UTC حتى يتطابقوا مع البيانات المخزنة في قاعدة البيانات
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(startOfDay.AddHours(3), tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(endOfDay.AddHours(3), tz);

            // 🔹 نأتي بالرسائل المرسلة خلال هذا اليوم فقط
            var logs = await _context.message_logs
                .Where(l => (l.Status == "sent" || l.Status == "deleted")
                            && l.UserId == userId
                            && l.AttemptedAt >= startUtc
                            && l.AttemptedAt < endUtc )
                .Select(l => l.Recipient)
                .ToListAsync();

            // 🔹 عدّ الأرقام العادية والمجموعات
            var numbersCount = logs.Count(r => !r.Contains("@g.us"));
            var groupsCount = logs.Count(r => r.Contains("@g.us"));

            return Ok(new
            {
                numbersCount,
                groupsCount,
                total = numbersCount + groupsCount
            });
        }


        [HttpGet("user/{userId}/messages-stats")]
        public async Task<IActionResult> GetUserMessagesStats(long userId, string range = "day")
        {
            var now = DateTime.Now.Date;

            var logs = await _context.message_logs
            .Where(l => l.UserId == userId
             && (l.Status == "sent" || l.Status == "deleted"))
            .Select(l => l.AttemptedAt)
            .ToListAsync();


            if (range == "day")
            {
                // 🔹 نحدد بداية الأسبوع (السبت) ونهايته (الجمعة)
                var today = DateTime.Now.Date;
                int daysSinceSaturday = ((int)today.DayOfWeek + 1) % 7; // السبت = 0
                var startOfWeek = today.AddDays(-daysSinceSaturday);
                var endOfWeek = startOfWeek.AddDays(6);

                // 🔹 نجهز الأيام من السبت إلى الجمعة
                var weekDays = Enumerable.Range(0, 7).Select(i => startOfWeek.AddDays(i)).ToList();

                // 🔹 نجهز البيانات من الـ logs
                var grouped = logs
                    .Where(d => d.Date >= startOfWeek && d.Date <= endOfWeek)
                    .GroupBy(d => d.Date)
                    .ToDictionary(g => g.Key, g => g.Count());

                // 🔹 نجهز النتيجة بنفس الصيغة
                var result = weekDays
                    .Select(d => new
                    {
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

                var now1 = DateTime.Now.Date;
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
                var months = Enumerable.Range(1, 12).ToList();
                var grouped = logs
                    .Where(d => d.Year == now.Year)
                    .GroupBy(d => d.Month)
                    .ToDictionary(g => g.Key, g => g.Count());

                var result = months
                    .Select(m => new {
                        Period = m.ToString("00"),
                        Count = grouped.ContainsKey(m) ? grouped[m] : 0
                    })
                    .ToList();

                return Ok(result);
            }
        }



    }
}
