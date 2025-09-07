using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserAccountsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly HttpClient _client;
        private readonly string _baseUrl;

        public UserAccountsController(AppDbContext context, IOptions<WasenderSettings> wasenderOptions)
        {
            _context = context;

            //  إعداد HttpClient مره واحده
            _baseUrl = wasenderOptions.Value.BaseUrl.TrimEnd('/');
            _client = new HttpClient
            {
                BaseAddress = new Uri(_baseUrl)
            };
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", wasenderOptions.Value.ApiKey);
        }

        [HttpPost("create-session")]
        public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequests req)
        {
            var existingAccount = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == req.UserId && a.PlatformId == req.PlatformId);

            // ✅ نفس الرقم موجود + عنده سيشن شغال
            if (existingAccount != null && existingAccount.AccountIdentifier == req.PhoneNumber && existingAccount.WasenderSessionId != null)
            {
                return Ok(new
                {
                    success = true,
                    message = "Account already exists with same phone",
                    sessionId = existingAccount.WasenderSessionId
                });
            }

            // 📌 لو الحساب موجود بس WasenderSessionId = null → اعمل سيشن جديد
            if (existingAccount != null && existingAccount.WasenderSessionId == null)
            {
                var bodyNewSession = JsonContent.Create(new
                {
                    name = req.Name,
                    phone_number = req.PhoneNumber,
                    log_messages = req.LogMessages,
                    account_protection = req.AccountProtection,
                    read_incoming_messages = true,
                });

                var respNew = await _client.PostAsync("/api/whatsapp-sessions", bodyNewSession);
                var createContentNew = await respNew.Content.ReadAsStringAsync();

                if (!respNew.IsSuccessStatusCode)
                    return StatusCode((int)respNew.StatusCode, new { success = false, error = createContentNew });

                var newData = JsonDocument.Parse(createContentNew);
                var newSessionId = newData.RootElement.GetProperty("data").GetProperty("id").GetInt32();
                var newApiKey = newData.RootElement.GetProperty("data").GetProperty("api_key").GetString();

                // تحديث الحساب الموجود
                existingAccount.AccountIdentifier = req.PhoneNumber;
                existingAccount.DisplayName = req.Name;
                existingAccount.Status = "disconnected";
                existingAccount.AccessToken = newApiKey;
                existingAccount.WasenderSessionId = newSessionId;
                existingAccount.ConnectedAt = DateTime.UtcNow;
                existingAccount.LastActivity = DateTime.UtcNow;
                existingAccount.QrCodeExpiry = DateTime.UtcNow.AddSeconds(45);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    sessionId = newSessionId,
                    message = "New session created because old session was null"
                });
            }

            // 📌 لو الحساب موجود لكن برقم مختلف
            if (existingAccount != null && existingAccount.AccountIdentifier != req.PhoneNumber)
            {
                var updateBody = new
                {
                    name = req.Name,
                    phone_number = req.PhoneNumber,
                    account_protection = req.AccountProtection,
                    log_messages = req.LogMessages,
                    read_incoming_messages = true,
                };

                var putResp = await _client.PutAsJsonAsync(
                    $"/api/whatsapp-sessions/{existingAccount.WasenderSessionId}",
                    updateBody
                );

                if (putResp.IsSuccessStatusCode)
                {
                    var putContent = await putResp.Content.ReadAsStringAsync();
                    var putData = JsonDocument.Parse(putContent);

                    var newApiKey = putData.RootElement.GetProperty("data").GetProperty("api_key").GetString();

                    existingAccount.AccountIdentifier = req.PhoneNumber;
                    existingAccount.DisplayName = req.Name;
                    existingAccount.AccessToken = newApiKey;
                    existingAccount.Status = "disconnected";
                    existingAccount.LastActivity = DateTime.UtcNow;
                    existingAccount.QrCodeExpiry = DateTime.UtcNow.AddSeconds(45);

                    await _context.SaveChangesAsync();

                    return Ok(new
                    {
                        success = true,
                        message = "Phone number updated successfully",
                        sessionId = existingAccount.WasenderSessionId
                    });
                }
                else
                {
                    // ❌ فشل التحديث → نحذف القديم ونعمل واحد جديد
                    _context.user_accounts.Remove(existingAccount);
                    await _context.SaveChangesAsync();
                }
            }

            // 🆕 مفيش حساب أصلاً → اعمل سيشن جديد
            var body = JsonContent.Create(new
            {
                name = req.Name,
                phone_number = req.PhoneNumber,
                log_messages = req.LogMessages,
                account_protection = req.AccountProtection,
                read_incoming_messages = true,
            });

            var resp = await _client.PostAsync("/api/whatsapp-sessions", body);
            var createContent = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, new { success = false, error = createContent });

            var data = JsonDocument.Parse(createContent);
            var sessionId = data.RootElement.GetProperty("data").GetProperty("id").GetInt32();
            var apiToken = data.RootElement.GetProperty("data").GetProperty("api_key").GetString();

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
                sessionId,
                message = "New session created successfully"
            });
        }


        [HttpGet("get-qr/{userId:int}/{platformId:int}")]
        public async Task<IActionResult> GetQrCode(int userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == userId && a.PlatformId == platformId);

            if (account == null || account.WasenderSessionId == null)
                return NotFound(new { success = false, message = "No session found for this user" });

            //  Connect session
            var connectResp = await _client.PostAsync(
                $"/api/whatsapp-sessions/{account.WasenderSessionId}/connect",
                null
            );
            var connectContent = await connectResp.Content.ReadAsStringAsync();

            if (!connectResp.IsSuccessStatusCode)
                return StatusCode((int)connectResp.StatusCode, new { success = false, error = connectContent });

            //  Get QR Code
            var qrResp = await _client.GetAsync($"/api/whatsapp-sessions/{account.WasenderSessionId}/qrcode");
            var qrContent = await qrResp.Content.ReadAsStringAsync();

            if (!qrResp.IsSuccessStatusCode)
                return StatusCode((int)qrResp.StatusCode, new { success = false, error = qrContent });

            var qrData = JsonDocument.Parse(qrContent);
            var qrCode = qrData.RootElement.GetProperty("data").GetProperty("qrCode").GetString();

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

            var resp = await _client.GetAsync($"/api/whatsapp-sessions/{account.WasenderSessionId}");
            var content = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, new { success = false, error = content });

            var data = JsonDocument.Parse(content);
            var status = data.RootElement.GetProperty("data").GetProperty("status").GetString();

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


        [HttpPost("logout/{userId:int}/{platformId:int}")]
        public async Task<IActionResult> Logout(int userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == userId && a.PlatformId == platformId);

            if (account == null || account.WasenderSessionId == null)
                return NotFound(new { success = false, message = "No active session found" });

            var resp = await _client.DeleteAsync($"/api/whatsapp-sessions/{account.WasenderSessionId}");
            var content = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                return StatusCode((int)resp.StatusCode, new
                {
                    success = false,
                    error = content
                });
            }

            account.Status = "disconnected";
            account.WasenderSessionId = null;
            account.AccessToken = null;
            account.LastActivity = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "User logged out and session disconnected"
            });
        }
    }
}
