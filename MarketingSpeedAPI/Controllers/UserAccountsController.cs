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
            var hasActiveSubscription = await _context.UserSubscriptions.AnyAsync(s =>
                s.UserId == req.UserId &&
                s.IsActive == true &&
                s.PaymentStatus == "paid" &&
                s.StartDate <= DateTime.UtcNow.Date &&
                s.EndDate >= DateTime.UtcNow.Date
            );

            if (!hasActiveSubscription)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "0"
                });
            }

            var existingAccount = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == req.UserId && a.PlatformId == req.PlatformId);

            if (existingAccount != null && existingAccount.AccountIdentifier == req.PhoneNumber && existingAccount.WasenderSessionId != null)
            {
                return Ok(new
                {
                    success = true,
                    message = "Account already exists with same phone",
                    sessionId = existingAccount.WasenderSessionId

                });
            }

            if (existingAccount != null && existingAccount.WasenderSessionId == null)
            {
                var bodyNewSession = JsonContent.Create(new
                {
                    name = req.Name,
                    phone_number = req.PhoneNumber,
                    log_messages = req.LogMessages,
                    account_protection =false,
                    read_incoming_messages = true,
                    webhook_url = "https://comedically-dcollet-elida.ngrok-free.app/api/webhook",
                    webhook_enabled = true,
                    webhook_events = new[]
    {
        "messages.received",
        "session.status",
        "messages.update",
        "message-receipt.update",
    "message.sent"
    }
                });

                var respNew = await _client.PostAsync("/api/whatsapp-sessions", bodyNewSession);
                var createContentNew = await respNew.Content.ReadAsStringAsync();

                if (!respNew.IsSuccessStatusCode)
                    return StatusCode((int)respNew.StatusCode, new { success = false, error = createContentNew });

                var newData = JsonDocument.Parse(createContentNew);
                var newSessionId = newData.RootElement.GetProperty("data").GetProperty("id").GetInt32();
                var newApiKey = newData.RootElement.GetProperty("data").GetProperty("api_key").GetString();
                var newWebhookSecret = newData.RootElement.GetProperty("data").GetProperty("webhook_secret").GetString();

                existingAccount.AccountIdentifier = req.PhoneNumber;
                existingAccount.DisplayName = req.Name;
                existingAccount.Status = "disconnected";
                existingAccount.AccessToken = newApiKey;
                existingAccount.WasenderSessionId = newSessionId;
                existingAccount.ConnectedAt = DateTime.UtcNow;
                existingAccount.LastActivity = DateTime.UtcNow;
                existingAccount.QrCodeExpiry = DateTime.UtcNow.AddSeconds(45);
                existingAccount.WebhookSecret = newWebhookSecret;


                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    sessionId = newSessionId,
                    message = "New session created because old session was null"
                });
            }

            if (existingAccount != null && existingAccount.AccountIdentifier != req.PhoneNumber)
            {
                var updateBody = new
                {
                    name = req.Name,
                    phone_number = req.PhoneNumber,
                    account_protection = false,
                    log_messages = req.LogMessages,
                    read_incoming_messages = true,
                    webhook_url = "https://comedically-dcollet-elida.ngrok-free.app/api/webhook",
                    webhook_enabled = true,
                    webhook_events = new[]
    {
        "messages.received",
        "session.status",
        "messages.update",
        "message-receipt.update",
    "message.sent"
    }
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
                    var newWebhookSecret = putData.RootElement.GetProperty("data").GetProperty("webhook_secret").GetString();

                    existingAccount.AccountIdentifier = req.PhoneNumber;
                    existingAccount.DisplayName = req.Name;
                    existingAccount.AccessToken = newApiKey;
                    existingAccount.Status = "disconnected";
                    existingAccount.LastActivity = DateTime.UtcNow;
                    existingAccount.QrCodeExpiry = DateTime.UtcNow.AddSeconds(45);
                    existingAccount.WebhookSecret = newWebhookSecret;
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
                    _context.user_accounts.Remove(existingAccount);
                    await _context.SaveChangesAsync();
                }
            }

            var body = JsonContent.Create(new
            {
                name = req.Name,
                phone_number = req.PhoneNumber,
                log_messages = req.LogMessages,
                account_protection = false,
                read_incoming_messages = true,
                webhook_url = "https://comedically-dcollet-elida.ngrok-free.app/api/webhook",
                webhook_enabled = true,
                webhook_events = new[]
{
    "messages.received",
    "session.status",
    "messages.update",
    "message-receipt.update",
    "message.sent"
}
            });

            var resp = await _client.PostAsync("/api/whatsapp-sessions", body);
            var createContent = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, new { success = false, error = createContent });

            var data = JsonDocument.Parse(createContent);
            var sessionId = data.RootElement.GetProperty("data").GetProperty("id").GetInt32();
            var apiToken = data.RootElement.GetProperty("data").GetProperty("api_key").GetString();
            var webhookSecret = data.RootElement.GetProperty("data").GetProperty("webhook_secret").GetString();

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
                QrCodeExpiry = DateTime.UtcNow.AddSeconds(45),
                WebhookSecret = webhookSecret
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

            var connectResp = await _client.PostAsync(
                $"/api/whatsapp-sessions/{account.WasenderSessionId}/connect",
                null
            );
            var connectContent = await connectResp.Content.ReadAsStringAsync();

            if (!connectResp.IsSuccessStatusCode)
                return StatusCode((int)connectResp.StatusCode, new { success = false, error = connectContent });

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
            var today = DateTime.UtcNow.Date;

            var subscription = await _context.UserSubscriptions
                .Where(s => s.UserId == userId &&
                            s.IsActive &&
                            s.PaymentStatus == "paid" &&
                            s.StartDate <= today &&
                            s.EndDate >= today)
                .OrderByDescending(s => s.EndDate)
                .FirstOrDefaultAsync();

            if (subscription == null)
            {
                return BadRequest(new
                {
                    success = false,
                     status = "Expired"
                });
            }

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
                status,
                subscriptionValidUntil = subscription.EndDate
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
