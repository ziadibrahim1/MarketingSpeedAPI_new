using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using RestSharp;
using System.Diagnostics;
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
        string CleanPhone(string x)
        {
            return new string(x.Where(char.IsDigit).ToArray());
        }


        [HttpPost("create-session")]
        public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequests req)
        {
            // 1) تحقق من الاشتراك
            var hasActiveSubscription = await _context.UserSubscriptions.AnyAsync(s =>
                s.UserId == req.UserId &&
                s.IsActive == true &&
                s.PaymentStatus == "paid" &&
                s.StartDate <= DateTime.UtcNow &&
                s.EndDate >= DateTime.UtcNow
            );

            if (!hasActiveSubscription)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "0"
                });
            }

            // 2) البحث عن حساب سابق
            var existingAccount = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == req.UserId &&
                                          a.PlatformId == req.PlatformId);

            
            if (existingAccount != null && existingAccount.AccessToken != null)
            {

                existingAccount.AccountIdentifier = req.PhoneNumber;
                existingAccount.Status = "disconnected";
                existingAccount.LastActivity = DateTime.UtcNow;
                existingAccount.QrCodeExpiry = DateTime.UtcNow.AddMinutes(5);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Account already exists",
                    sessionId = existingAccount.channelId,
                    token = existingAccount.AccessToken
                });
            }


            // 3) WHAPI — Create Channel
            var client = new RestClient("https://manager.whapi.cloud/channels");
            var request = new RestRequest("", Method.Put);
            request.AddHeader("accept", "application/json");
            request.AddHeader("authorization", $"Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6ImExZDI2YWYyYmY4MjVmYjI5MzVjNWI3OTY3ZDA3YmYwZTMxZWIxYjcifQ.eyJwYXJ0bmVyIjp0cnVlLCJpc3MiOiJodHRwczovL3NlY3VyZXRva2VuLmdvb2dsZS5jb20vd2hhcGktYTcyMWYiLCJhdWQiOiJ3aGFwaS1hNzIxZiIsImF1dGhfdGltZSI6MTc2NDAwODM3MywidXNlcl9pZCI6InBYcXk4RkpuOFVoY1g0WjUydHNwdkc4UUpqcjEiLCJzdWIiOiJwWHF5OEZKbjhVaGNYNFo1MnRzcHZHOFFKanIxIiwiaWF0IjoxNzY0MDA4MzczLCJleHAiOjE4MjQ0ODgzNzMsImVtYWlsIjoibWFya3Rpbmcuc3BlZWRAZ21haWwuY29tIiwiZW1haWxfdmVyaWZpZWQiOnRydWUsImZpcmViYXNlIjp7ImlkZW50aXRpZXMiOnsiZW1haWwiOlsibWFya3Rpbmcuc3BlZWRAZ21haWwuY29tIl19LCJzaWduX2luX3Byb3ZpZGVyIjoicGFzc3dvcmQifX0.DXkDBDOydqV2XkRqBK9-15SZUT-ADEdHgebhIrM-cdoigtG2mYtQYhbVKnamSX86Pe4-zWgefQAUXj1DmTykp0OwhSBN_bEGuUikmwxcJnsl-nCu1azUbvADsUi9S_lKInurHsl7U_j70iOEIF_FkBEnzd7SnvxkpvHaVqNn5-DFLH_L2wzsuyM4WwJVwydqEuR_df4V3U0Bkk7abb_xg1nHbwFgGY3S57w5E4V2BAQfi-gyo4CZmimD5XAwgPFPpnH9jBWlDEWbPE_LbYkSRllByrrgdtO0WjU_aYFVD903TETspHTq1oQ1_okrUsiJDKqsMzGPufpRpu9fYweGag");
            request.AddJsonBody("{\"webhooks\":[{\"mode\":\"body\",\"url\":\"http://marketingspeed.online/api/webhook\"}],\"offline_mode\":false,\"full_history\":true}", false);
            request.AddJsonBody("{\"offline_mode\":false,\"full_history\":true}", false);
            request.AddJsonBody(new
            {
                name = req.PhoneNumber,
                phone = CleanPhone(req.PhoneNumber),
                projectId = "K8widAUASFVsnLtMChuh"
            });

            var response = await client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                return StatusCode((int)response.StatusCode, new
                {
                    success = false,
                    error = response.Content
                });
            }


            // 4) قراءة الاستجابة الحقيقية من WHAPI
            var json = JObject.Parse(response.Content);

            string channelId = json["id"]?.ToString();
            string token = json["token"]?.ToString();
            long? creationTs = json["creationTS"]?.ToObject<long>();

            if (string.IsNullOrEmpty(channelId) || string.IsNullOrEmpty(token))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "Invalid WHAPI response"
                });
            }


            // 5) إنشاء أو تحديث الحساب
            if (existingAccount == null)
            {
                existingAccount = new UserAccount
                {
                    UserId = req.UserId,
                    PlatformId = req.PlatformId,

                    // تخزين بيانات WHAPI في المتغيرات القديمة
                    AccountIdentifier = req.PhoneNumber,
                    DisplayName = req.Name,

                    channelId = channelId,
                    AccessToken = token,
                    WebhookSecret = "", // WHAPI لا يرجع secret

                    Status = "disconnected",
                    ConnectedAt = creationTs.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(creationTs.Value).UtcDateTime : DateTime.UtcNow,
                    LastActivity = DateTime.UtcNow,

                    QrCodeExpiry = DateTime.UtcNow.AddMinutes(5) // وقت مؤقت
                };

                _context.user_accounts.Add(existingAccount);
            }
            else
            {
                existingAccount.AccountIdentifier = req.PhoneNumber;
                existingAccount.DisplayName = req.Name;

                existingAccount.channelId = channelId;
                existingAccount.AccessToken = token;
                existingAccount.WebhookSecret = "";

                existingAccount.Status = "disconnected";
                existingAccount.LastActivity = DateTime.UtcNow;
                existingAccount.QrCodeExpiry = DateTime.UtcNow.AddMinutes(5);
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                sessionId = existingAccount.channelId,
                token = existingAccount.AccessToken,
                message = "Channel created successfully"
            });
        }
        [HttpGet("send-login-code/{userId:int}/{platformId:int}/{phone}")]
        public async Task<IActionResult> SendLoginCode(int userId, int platformId, string phone)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == userId && a.PlatformId == platformId);

            if (account == null || string.IsNullOrEmpty(account.AccessToken))
            {
                return BadRequest(new { success = false, message = "No session or AccessToken" });
            }

            var client = new RestClient($"https://gate.whapi.cloud/users/login/{phone}");
            var request = new RestRequest("", Method.Get);
            request.AddHeader("accept", "application/json");
            request.AddHeader("authorization", $"Bearer {account.AccessToken}");

            var response = await client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                return StatusCode((int)response.StatusCode, new
                {
                    success = false,
                    error = response.Content
                });
            }

            var json = JObject.Parse(response.Content);

            string? code = json["code"]?.ToString();

            if (code == null)
                return BadRequest(new { success = false, message = "Invalid response" });

            return Ok(new
            {
                success = true,
                code = code // ← الكود اللي هيمسحه المستخدم من الواتساب
            });
        }
        [HttpPost("confirm-login-code")]
        public async Task<IActionResult> ConfirmLoginCode([FromBody] ConfirmLoginRequest req)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == req.UserId && a.PlatformId == req.PlatformId);

            if (account == null || string.IsNullOrEmpty(account.AccessToken))
                return BadRequest(new { success = false, message = "No access token" });

            var client = new RestClient("https://gate.whapi.cloud/users/login");
            var request = new RestRequest("", Method.Post);
            request.AddHeader("accept", "application/json");
            request.AddHeader("authorization", $"Bearer {account.AccessToken}");
            request.AddJsonBody(new
            {
                phone = req.Phone,
                code = req.Code
            });

            var response = await client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                return StatusCode((int)response.StatusCode, new
                {
                    success = false,
                    error = response.Content
                });
            }

            // النجاح هنا يعني أن الحساب تم ربطه بنجاح
            account.Status = "connected";
            account.LastActivity = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Login completed" });
        }


        [HttpGet("get-qr/{userId:int}/{platformId:int}")]
        public async Task<IActionResult> GetQrCode(int userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == userId && a.PlatformId == platformId);

            if (account == null || account.channelId == null || string.IsNullOrEmpty(account.AccessToken))
            {
                return NotFound(new
                {
                    success = false,
                    message = "No session found for this user"
                });
            }

            // WHAPI QR endpoint
            var client = new RestClient("https://gate.whapi.cloud/users/login/rowdata?wakeup=true");
            var request = new RestRequest("", Method.Get);

            request.AddHeader("accept", "application/json");
            request.AddHeader("authorization", $"Bearer {account.AccessToken}");

            var response = await client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                return StatusCode((int)response.StatusCode, new
                {
                    success = false,
                    error = response.Content
                });
            }

            var json = JObject.Parse(response.Content);

            string? rowdata = json["rowdata"]?.ToString();
            if (string.IsNullOrEmpty(rowdata))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "Invalid WHAPI QR response"
                });
            }

            // 🔥 إعادة نفس الشكل القديم بدون تغيير
            return Ok(new
            {
                success = true,
                qrCode = rowdata,   // اسم المتغير القديم كما هو
                message = "OK"
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

            if (account == null || account.channelId == null)
                return NotFound(new { success = false, message = "No session found" });

            // WHAPI health API
            var client = new RestClient("https://gate.whapi.cloud/health?wakeup=true&channel_type=web");
            var request = new RestRequest("", Method.Get);
            request.AddHeader("accept", "application/json");
            request.AddHeader("authorization", $"Bearer {account.AccessToken}");

            var response = await client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                return StatusCode((int)response.StatusCode, new
                {
                    success = false,
                    error = response.Content
                });
            }

            var json = JObject.Parse(response.Content);
            int code = json["status"]?["code"]?.ToObject<int>() ?? 3;
            string text = json["status"]?["text"]?.ToString() ?? "QR";

            // تحويل WHAPI status إلى نفس النظام القديم
            string mappedStatus = "disconnected";

            if (code == 4) // AUTH
                mappedStatus = "connected";
            else if (code == 3) // QR
                mappedStatus = "disconnected";
            else
                mappedStatus = "disconnected";

            
            // تحديث قاعدة البيانات بنفس المتغيرات القديمة
            account.Status = mappedStatus;
            account.LastActivity = DateTime.UtcNow;

            if (mappedStatus == "connected")
            {
                account.ConnectedAt = DateTime.UtcNow;

                // ====== PATCH لإرسال إعدادات البروكسي بعد التأكد من الاتصال ======
                try
                {
                    var options = new RestClientOptions("https://gate.whapi.cloud/settings");
                    var patchClient = new RestClient(options);

                    var patchRequest = new RestRequest("", Method.Patch);
                    patchRequest.AddHeader("accept", "application/json");
                    patchRequest.AddHeader("authorization", $"Bearer {account.AccessToken}");

                    patchRequest.AddJsonBody(new
                    {
                        offline_mode = false,
                        full_history = true,
                    });

                    // تنفيذ الطلب في الخلفية بدون التأثير على الريسبونس
                    _ = patchClient.ExecuteAsync(patchRequest);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Proxy Patch Error: " + ex.Message);
                    // لا نرجع خطأ للفلاتر – فقط نسجل الخطأ إذا أردت
                }
                // ===========================================================
            }

            await _context.SaveChangesAsync();

            // الريسبونس كما هو بدون أي تغيير
            return Ok(new
            {
                success = true,
                status = mappedStatus,
                subscriptionValidUntil = subscription.EndDate
            }); 
        }


        [HttpPost("logout/{userId:int}/{platformId:int}")]
        public async Task<IActionResult> Logout(int userId, int platformId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == userId && a.PlatformId == platformId);

            if (account == null || account.Status == "disconnected")
                return NotFound(new { success = false, message = "No active session found" });

            // WHAPI logout
            var options = new RestClientOptions("https://gate.whapi.cloud/users/logout");
            var client = new RestClient(options);

            var request = new RestRequest("", Method.Post);
            request.AddHeader("accept", "application/json");
            request.AddHeader("authorization", $"Bearer {account.AccessToken}");

            var resp = await client.ExecuteAsync(request);
            var content = resp.Content;

            if (!resp.IsSuccessful)
            {
                return StatusCode((int)resp.StatusCode, new
                {
                    success = false,
                    error = content
                });
            }

            // تحديث بيانات DB بنفس الشكل القديم
            account.Status = "disconnected";
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
