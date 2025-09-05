using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserAccountsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public UserAccountsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("create-session")]
        public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequests req)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "821|bd5KxyUwMWKWWa7yOpYlOLkcfOdanS52gufIvNXB2fae0c2b");

            var body = JsonContent.Create(new
            {
                name = req.Name,
                phone_number = req.PhoneNumber,
                log_messages = req.LogMessages,
                account_protection = req.AccountProtection,
                read_incoming_messages = true
            });

            var resp = await client.PostAsync("https://www.wasenderapi.com/api/whatsapp-sessions", body);
            var createContent = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, new { success = false, error = createContent });

            var data = JsonDocument.Parse(createContent);
            var sessionId = data.RootElement.GetProperty("data").GetProperty("id").GetInt32();
            var apiToken = data.RootElement.GetProperty("data").GetProperty("api_key").GetString();
            // 🗄️ تخزين السيشن في قاعدة البيانات مع wasenderSessionId
            var account = new UserAccount
            {
                UserId = req.UserId,
                PlatformId = req.PlatformId,
                AccountIdentifier = req.PhoneNumber,
                DisplayName = req.Name,
                Status = "disconnected",
                AccessToken = apiToken,
                WasenderSessionId = sessionId,
                ConnectedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                QrCodeExpiry = DateTime.UtcNow.AddSeconds(45)
            };
            _context.user_accounts.Add(account);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                sessionId
            });
        }


        [HttpGet("get-qr/{userId:int}/{platformId:int}")]
        public async Task<IActionResult> GetQrCode(int userId, int platformId)
        {
            // 🗄️ البحث عن السيشن في قاعدة البيانات
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == userId && a.PlatformId == platformId);

            if (account == null || account.WasenderSessionId == null)
                return NotFound(new { success = false, message = "No session found for this user" });

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "821|bd5KxyUwMWKWWa7yOpYlOLkcfOdanS52gufIvNXB2fae0c2b");

            // ✨ الخطوة 1: Connect session في wasender
            var connectResp = await client.PostAsync(
                $"https://www.wasenderapi.com/api/whatsapp-sessions/{account.WasenderSessionId}/connect",
                null
            );
            var connectContent = await connectResp.Content.ReadAsStringAsync();

            if (!connectResp.IsSuccessStatusCode)
                return StatusCode((int)connectResp.StatusCode, new { success = false, error = connectContent });

            // ✨ الخطوة 3: استدعاء waSender لجلب QR باستخدام wasenderSessionId

            var qrResp = await client.GetAsync($"https://www.wasenderapi.com/api/whatsapp-sessions/{account.WasenderSessionId}/qrcode");
            var qrContent = await qrResp.Content.ReadAsStringAsync();

            if (!qrResp.IsSuccessStatusCode)
                return StatusCode((int)qrResp.StatusCode, new { success = false, error = qrContent });

            var qrData = JsonDocument.Parse(qrContent);
            var qrCode = qrData.RootElement.GetProperty("data").GetProperty("qrCode").GetString();

            // ✨ الخطوة 4: إرجاع النتيجة
            
            return Ok(new
            {
                success = true,
                qrCode
            });
        }


        [HttpGet("check-status/{userId:int}/{platformId:int}")]
        public async Task<IActionResult> CheckStatus(int userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == userId && a.PlatformId == platformId);

            if (account == null || account.WasenderSessionId == null)
                return NotFound(new { success = false, message = "No session found" });

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "821|bd5KxyUwMWKWWa7yOpYlOLkcfOdanS52gufIvNXB2fae0c2b");

            var resp = await client.GetAsync($"https://www.wasenderapi.com/api/whatsapp-sessions/{account.WasenderSessionId}");
            var content = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, new { success = false, error = content });

            var data = JsonDocument.Parse(content);
            var status = data.RootElement.GetProperty("data").GetProperty("status").GetString();

            // تحديث قاعدة البيانات
            account.Status = status;
            account.LastActivity = DateTime.UtcNow;
            if (status == "connected")
                account.ConnectedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                status
            });
        }


    }

}
