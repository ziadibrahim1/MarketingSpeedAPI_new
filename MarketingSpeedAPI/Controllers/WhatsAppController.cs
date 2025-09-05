using Microsoft.AspNetCore.Mvc;
using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WhatsAppController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpFactory;
        private readonly string _wasenderApiKey;

        public WhatsAppController(AppDbContext context, IHttpClientFactory httpFactory, IConfiguration config)
        {
            _context = context;
            _httpFactory = httpFactory;
            _wasenderApiKey = config["Wasender:ApiKey"];
        }

        [HttpPost("create-session")]
        public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequest req)
        {
            var client = _httpFactory.CreateClient();
            client.BaseAddress = new Uri("https://www.wasenderapi.com");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _wasenderApiKey);

            var body = new
            {
                name = req.DisplayName ?? $"user-{req.UserId}",
                phone_number = req.PhoneNumber,
                log_messages = true,
                account_protection = true,
                read_incoming_messages = true
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/whatsapp-sessions", jsonContent);

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, new { success = false, message = await response.Content.ReadAsStringAsync() });

            var data = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

            var sessionId = data.GetProperty("data").GetProperty("id").GetInt32();

            // حفظ الجلسة في قاعدة البيانات
            var account = new UserAccount
            {
                UserId = req.UserId,
                PlatformId = req.PlatformId,
                AccountIdentifier = req.PhoneNumber,
                DisplayName = req.DisplayName,
                Status = "connected"
            };

            _context.user_accounts.Add(account);
            await _context.SaveChangesAsync();

            // جلب QR
            var qrResp = await client.GetAsync($"/api/whatsapp-sessions/{sessionId}/qrcode");
            qrResp.EnsureSuccessStatusCode();
            var qrData = JsonSerializer.Deserialize<JsonElement>(await qrResp.Content.ReadAsStringAsync());

            return Ok(new
            {
                success = true,
                sessionId,
                qrUrl = qrData.GetProperty("data").GetString()
            });
        }

        [HttpGet("qr/{userId:int}/{platformId:int}")]
        public IActionResult GetQr(int userId, int platformId)
        {
            var qrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=200x200&data=whatsapp-{userId}-{platformId}";
            return Ok(new { success = true, qrUrl });
        }
    }
}
