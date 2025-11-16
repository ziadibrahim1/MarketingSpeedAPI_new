using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.DTOs;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly EmailService _emailService;
        private static readonly Random _random = new Random();
        private readonly AppDbContext _context;

        public AuthController(EmailService emailService, AppDbContext context)
        {
            _emailService = emailService;
            _context = context;
        }

        private string ComputeSha256Hash(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                // ComputeHash - returns byte array
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                // Convert byte array to a string
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
        
        [HttpGet("SystemAlert/latest")]
        public async Task<IActionResult> GetLatestSystemAlert([FromQuery] string platform, [FromQuery] string currentVersion)
        {
            var alert = await _context.system_alerts
                .Where(a => a.Platform == platform)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync();

            if (alert == null)
                return Ok(new { success = false });

            bool requiresUpdate = !string.IsNullOrEmpty(alert.MinAppVersion)
                                  && string.Compare(currentVersion, alert.MinAppVersion) < 0;

            return Ok(new
            {
                success = true,
                alert.Id,
                alert.Title,
                alert.Message,
                alert.ButtonText,
                alert.ActionUrl,
                alert.IsMandatory,
                alert.ShowOnce,
                alert.MinAppVersion,
                alert.LatestVersion,
                requiresUpdate
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var lang = (dto.language ?? "en").ToLower();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.email == dto.email && u.is_email_verified);

            if (user == null)
            {
                var msg = lang == "ar" ? "البريد الإلكتروني غير موجود" : "Email not found";
                return Unauthorized(new { message = msg });
            }

            var hashedPassword = ComputeSha256Hash(dto.password_hash);
            Console.WriteLine($"Hashed: {hashedPassword}");

            if (user.password_hash != hashedPassword)
            {
                var msg = lang == "ar" ? "كلمة المرور غير صحيحة" : "Incorrect password";
                return Unauthorized(new { message = msg });
            }

            if (!user.is_email_verified)
            {
                var msg = lang == "ar" ? "الحساب لم يتم تفعيله بعد" : "Account not verified yet";
                return Unauthorized(new { message = msg });
            }

            // تحديث آخر ظهور
            user.last_seen = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var successMsg = lang == "ar" ? "تم تسجيل الدخول بنجاح" : "Login successful";

            return Ok(new
            {
                message = successMsg,
                user = new
                {
                    user.id,
                    user.email,
                    user.first_name,
                    user.middle_name,
                    user.last_name,
                    user.user_type,
                    user.company_name,
                    user.profile_picture,
                    user.language,
                    user.theme,
                    user.status,
                    user.last_seen
                }

            });

        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            try
            {
                if (await _context.Users.AnyAsync(u => u.email == dto.email))
                    return BadRequest(new { error = "Email already exists" });

                if (string.IsNullOrWhiteSpace(dto.password_hash))
                    return BadRequest(new { error = "كلمة المرور مطلوبة" });

                // توليد كود عشوائي من 6 أرقام
                string code = _random.Next(100000, 999999).ToString();

                var user = new User
                {
                    first_name = dto.first_name,
                    middle_name = dto.middle_name,
                    last_name = dto.last_name,
                    email = dto.email,
                    country_code = dto.country_code,
                    phone = dto.phone,
                    country = dto.country,
                    city = dto.city,
                    user_type = dto.user_type,
                    company_name = dto.company_name,
                    description = dto.description,
                    language = dto.language ?? "ar",
                    theme = dto.theme ?? "light",
                    status = "active",
                    accept_notifications = dto.accept_notifications,
                    accept_terms = dto.accept_terms,
                    password_hash = ComputeSha256Hash(dto.password_hash),
                    verification_code = code,
                    verification_code_expires_at = DateTime.UtcNow.AddMinutes(2),
                    created_at = DateTime.UtcNow,
                    is_email_verified = false
                };

                // 1️⃣ احفظ اليوزر الأول
                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                // 2️⃣ ابعت الإيميل بعد الحفظ
                var emailSent = await _emailService.SendVerificationEmailAsync(user.email, code);
                if (!emailSent)
                {
                    // لو حابب تلغي التسجيل لو فشل الإيميل
                    _context.Users.Remove(user);
                    await _context.SaveChangesAsync();

                    return StatusCode(500, new { error = "فشل إرسال بريد التحقق" });
                }

                // 3️⃣ رجع الرد للفرونت
                return Ok(new
                {
                    message = "تم التسجيل. تم إرسال رمز التحقق إلى بريدك الإلكتروني.",
                    userId = user.id
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }

        [HttpPut("update-profile/{id}")]
        public async Task<IActionResult> UpdateProfile(int id, [FromBody] UpdateProfileDto dto)
        {
            var user = await _context.Users.FindAsync(id);

            if (user == null)
                return NotFound(new { message = "User not found" });

            // تحديث البيانات الشخصية
            if (!string.IsNullOrEmpty(dto.First_Name)) user.first_name = dto.First_Name;
            if (!string.IsNullOrEmpty(dto.Middle_Name)) user.middle_name = dto.Middle_Name;
            if (!string.IsNullOrEmpty(dto.Last_Name)) user.last_name = dto.Last_Name;
            if (!string.IsNullOrEmpty(dto.Country_Code)) user.country_code = dto.Country_Code;
             user.country = dto.CountryId.ToString();
            if (!string.IsNullOrEmpty(dto.Phone)) user.phone = dto.Phone;
            if (!string.IsNullOrEmpty(dto.User_Type)) user.user_type = dto.User_Type;
            if (!string.IsNullOrEmpty(dto.Company_Name)) user.company_name = dto.Company_Name;
            if (!string.IsNullOrEmpty(dto.Description)) user.description = dto.Description;
            if (!string.IsNullOrEmpty(dto.Profile_Picture)) user.profile_picture = dto.Profile_Picture;
            if (dto.Accept_Notifications.HasValue) user.accept_notifications = dto.Accept_Notifications.Value;
            if (dto.Accept_Terms.HasValue) user.accept_terms = dto.Accept_Terms.Value;
            if (!string.IsNullOrEmpty(dto.Language)) user.language = dto.Language;
            if (!string.IsNullOrEmpty(dto.Theme)) user.theme = dto.Theme;
            if (dto.last_seen.HasValue) user.last_seen = dto.last_seen;

            // تحديث المدينة وربطها بالدولة
            if (!string.IsNullOrEmpty(dto.CityName) && dto.CountryId.HasValue)
            {
                // التأكد من وجود الدولة
                var country = await _context.Countries
                    .Include(c => c.Cities)
                    .FirstOrDefaultAsync(c => c.Id == dto.CountryId.Value && c.IsActive);

                if (country == null)
                    return BadRequest(new { message = "Country not found" });

                // البحث عن المدينة داخل الدولة
                var city = country.Cities.FirstOrDefault(c =>
    c.IsActive &&
    ((int.TryParse(dto.CityName, out var cityId) && c.Id == cityId) ||
     c.NameEn == dto.CityName ||
     c.NameAr == dto.CityName)
);

                if (city == null)
                {
                    // إنشاء المدينة الجديدة
                    city = new City
                    {
                        NameAr = dto.CityName,  
                        NameEn = dto.CityName,
                        CountryId = country.Id,
                        IsActive = true
                    };
                    _context.Cities.Add(city);
                    await _context.SaveChangesAsync();
                }

                // ربط المستخدم بالمدينة الجديدة أو الموجودة
                user.city = city.Id;
            }

            user.updated_at = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Profile updated successfully", user });
        }


        [HttpPut("update-profile-settings/{id}")]
        public async Task<IActionResult> UpdateProfileSettings(int id, [FromBody] UpdateProfileDto dto)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound(new { message = "User not  found" });

            var emailChanged = false;
            var oldEmail = user.email;

            if (!string.IsNullOrEmpty(dto.First_Name)) user.first_name = dto.First_Name;
            if (!string.IsNullOrEmpty(dto.Middle_Name)) user.middle_name = dto.Middle_Name;
            if (!string.IsNullOrEmpty(dto.Last_Name)) user.last_name = dto.Last_Name;
            if (!string.IsNullOrEmpty(dto.Country_Code)) user.country_code = dto.Country_Code;
            if (!string.IsNullOrEmpty(dto.Country)) user.country = dto.Country;
            if (!string.IsNullOrEmpty(dto.Phone)) user.phone = dto.Phone;
            if (!string.IsNullOrEmpty(dto.User_Type)) user.user_type = dto.User_Type;
            if (!string.IsNullOrEmpty(dto.Company_Name)) user.company_name = dto.Company_Name;
            if (!string.IsNullOrEmpty(dto.Description)) user.description = dto.Description;
            if (!string.IsNullOrEmpty(dto.Profile_Picture)) user.profile_picture = dto.Profile_Picture;
            if (dto.Accept_Notifications.HasValue) user.accept_notifications = dto.Accept_Notifications.Value;
            if (dto.Accept_Terms.HasValue) user.accept_terms = dto.Accept_Terms.Value;
            if (!string.IsNullOrEmpty(dto.Language)) user.language = dto.Language;
            if (!string.IsNullOrEmpty(dto.Theme)) user.theme = dto.Theme;
            if (dto.last_seen.HasValue) user.last_seen = dto.last_seen;

            if (!string.IsNullOrEmpty(dto.Password))
                user.password_hash = ComputeSha256Hash(dto.Password);

            if (!string.IsNullOrEmpty(dto.Email) && dto.Email != user.email)
            {
                user.email = dto.Email;
                emailChanged = true;

                string code = new Random().Next(100000, 999999).ToString();
                user.verification_code = code;
                user.verification_code_expires_at = DateTime.UtcNow.AddMinutes(2);
                user.is_email_verified = false;

                await _emailService.SendVerificationEmailAsync(user.email, code);
            }

            if (!string.IsNullOrEmpty(dto.CityName) && dto.CityName.All(char.IsLetter))
            {
                var country = await _context.Countries
                    .Include(c => c.Cities)
                    .FirstOrDefaultAsync(c => c.Id == int.Parse(dto.Country) && c.IsActive);

                if (country == null)
                    return BadRequest(new { message = "Country not found" });

                var city = country.Cities.FirstOrDefault(c =>
                    c.IsActive &&
                    (c.NameAr == dto.CityName || c.NameEn == dto.CityName)
                );

                if (city == null)
                {
                    city = new City
                    {
                        NameAr = dto.CityName,
                        NameEn = dto.CityName,
                        CountryId = country.Id,
                        IsActive = true
                    };
                    _context.Cities.Add(city);
                    await _context.SaveChangesAsync();
                }

                user.city = city.Id;
            }
            else if (dto.City != null)
            {
                user.city = dto.City;
            }
            else if (!string.IsNullOrEmpty(dto.CityName) && !dto.CityName.All(char.IsLetter))
            {
                
                    user.city = int.Parse(dto.CityName);
               
            }
                user.updated_at = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            
            return Ok(new
            {
                message = "Profile updated successfully",
                user,
                emailChanged // ← frontend يستخدمه لتوجيه المستخدم لشاشة التحقق
            });
        }

        [HttpPut("reset-profile-settings/{id}")]
        public async Task<IActionResult> resetProfileSettings(int id, [FromBody] UpdateProfileDto dto)
        {
            var user = await _context.Users.FindAsync(id);
                user.email = dto.Email;
                user.is_email_verified = true;
            await _context.SaveChangesAsync();
            return Ok(new
            {
                message = "Profile reset successfully",
                user,
            });
        }

        [HttpPost("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyCodeDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.email == dto.email);
            if (user == null)
                return NotFound(new { error = "المستخدم غير موجود" });

            if (user.verification_code != dto.verification_code)
                return BadRequest(new { error = "رمز التحقق غير صحيح" });

            if (user.verification_code_expires_at < DateTime.UtcNow)
                return BadRequest(new { error = "انتهت صلاحية رمز التحقق" });

            // تم التحقق بنجاح
            user.is_email_verified = true;
            user.verification_code = null;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "تم التحقق من البريد الإلكتروني بنجاح",
                userId = user.id // ← هنا نرجع الـ id للمستخدم
            });
        }

        [HttpPost("delete-user")]
        public async Task<IActionResult> DeleteUser([FromBody] DeleteUserDto dto)
        {
            if (string.IsNullOrEmpty(dto.Email))
                return BadRequest(new { error = "Email is required" });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.email == dto.Email);
            if (user == null)
                return NotFound(new { error = "المستخدم غير موجود" });

            bool hasPackageLogs = await _context.PackageLogs.AnyAsync(pl => pl.UserId == user.id);
            bool hasMessages = await _context.Messages.AnyAsync(m => m.UserId == user.id);

            if (hasPackageLogs || hasMessages)
            {
                return BadRequest(new { error = "لا يمكن حذف المستخدم لأنه مرتبط بسجلات أخرى" });
            }

            try
            {
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                return Ok(new { message = "تم حذف المستخدم بنجاح" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "حدث خطأ أثناء حذف المستخدم", details = ex.Message });
            }
        }

        [HttpPost("resend-code")]
        public async Task<IActionResult> ResendVerificationCode([FromBody] EmailDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.email == dto.email);
            if (user == null)
                return NotFound(new { error = "المستخدم غير موجود" });

            if (user.is_email_verified)
                return BadRequest(new { error = "البريد الإلكتروني تم التحقق منه بالفعل" });

            string code = _random.Next(100000, 999999).ToString();
            user.verification_code = code;
            user.verification_code_expires_at = DateTime.UtcNow.AddMinutes(2);

            await _emailService.SendVerificationEmailAsync(user.email, code);
            await _context.SaveChangesAsync();

            return Ok(new { message = "تم إرسال كود تحقق جديد إلى البريد الإلكتروني" });
        }

        [HttpGet("GetCountries")]
        public async Task<IActionResult> GetCountries(string lang = "en")
        {
            lang = lang.ToLower();

            // ترتيب مخصص
            var gulfCountries = new List<string> { "الإمارات", "الكويت", "قطر", "عمان", "البحرين", "اليمن" };

            var countries = await _context.Countries
                .Where(c => c.IsActive)
                .Select(c => new
                {
                    Id = c.Id,
                    Name = lang == "ar" ? c.NameAr : c.NameEn,
                    c.IsoCode,
                    c.PhoneCode,
                    NameAr = c.NameAr
                })
                .ToListAsync();

            // ترتيب القائمة في الذاكرة
            var orderedCountries = countries
                .OrderBy(c => c.NameAr == "السعودية" ? 0 :
                              gulfCountries.Contains(c.NameAr) ? 1 : 2) // السعودية أولًا، دول الخليج ثانيًا، باقي الدول ثالثًا
                .ThenBy(c => c.Name) // ترتيب أبجدي حسب اللغة المطلوبة داخل كل مجموعة
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.IsoCode,
                    c.PhoneCode
                })
                .ToList();

            return Ok(orderedCountries);
        }

        [HttpGet("GetCities")]
        public async Task<IActionResult> GetCities(int countryId, string lang = "ar")
        {
            var cities = await _context.Cities
                .Where(c => c.CountryId == countryId && c.IsActive)
                .Select(c => new {
                    id = c.Id,
                    name = lang.ToLower() == "ar" ? c.NameAr : c.NameEn
                })
                .ToListAsync();
 
            var filteredCities = cities
                .Where(c => !int.TryParse(c.name, out _))
                .ToList();

            return Ok(filteredCities);
        }


        [HttpGet("GetTerms")]
        public async Task<IActionResult> GetTerms(string lang = "en")
        {
            var term = await _context.TermsAndConditions
                .Where(t => t.IsActive)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (term == null)
                return NotFound();

            var content = lang.ToLower() == "ar" ? term.ContentAr : term.ContentEn;

            return Ok(new { content });
        }

        // إرسال كود التحقق
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.email == dto.Email);
            if (user == null)
                return NotFound(new { message = "Email not found" });

            // توليد كود عشوائي 6 أرقام
            var code = new Random().Next(100000, 999999).ToString();
            user.verification_code = code;
            user.verification_code_expires_at = DateTime.UtcNow.AddMinutes(5);
            await _emailService.SendVerificationEmailAsync(user.email, code);

            await _context.SaveChangesAsync();

            return Ok(new { message = "Verification code sent to email" });
        }

        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto dto)
        {
            if (string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.Code))
                return BadRequest(new { message = "Email and Code are required" });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.email == dto.Email);

            if (user == null)
                return BadRequest(new { message = "User not found" });

            if (user.verification_code == null)
                return BadRequest(new { message = "No verification code found" });

            if (user.verification_code != dto.Code)
                return BadRequest(new { message = "Invalid verification code" });

            if (user.verification_code_expires_at < DateTime.UtcNow)
                return BadRequest(new { message = "Verification code expired" });

            // ✅ لو الكود صحيح
            user.is_email_verified = true;
            user.verification_code = null; // نمسح الكود بعد التحقق
            user.updated_at = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Verification successful" });
        }

        [HttpPost("resend-otp-code")]
        public async Task<IActionResult> ResendOtpCode([FromBody] EmailDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.email == dto.email);
            if (user == null)
                return NotFound(new { error = "المستخدم غير موجود" });

            
            string code = _random.Next(100000, 999999).ToString();
            user.verification_code = code;
            user.verification_code_expires_at = DateTime.UtcNow.AddMinutes(2);

            await _emailService.SendVerificationEmailAsync(user.email, code);
            await _context.SaveChangesAsync();

            return Ok(new { message = "تم إرسال كود تحقق جديد إلى البريد الإلكتروني" });
        }

        // إعادة تعيين كلمة المرور
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            if (string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.New_Password))
                return BadRequest(new { message = "Email and password are required" });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.email == dto.Email);

            if (user == null)
                return BadRequest(new { message = "User not found" });

            if (user.is_email_verified == false)
                return BadRequest(new { message = "Email not verified" });

            // 🔑 تشفير كلمة المرور الجديدة
            user.password_hash = ComputeSha256Hash(dto.New_Password);
            user.updated_at = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Password reset successfully" });
        }

        [HttpGet("getUserBy")]
        public async Task<IActionResult> GetUserById(int id, string lang)
        {
            var user = await (
                from u in _context.Users
                join c in _context.Cities
                    on u.city equals c.Id into cityGroup
                from c in cityGroup.DefaultIfEmpty()   // 👈 Left Join

                join acc in _context.user_accounts
                    on u.id equals acc.UserId into accountGroup
                from acc in accountGroup
                    .Where(a => a.PlatformId == 1)     // 👈 فلترة على platformid = 1
                    .DefaultIfEmpty()

                where u.id == id
                select new
                {
                    u.id,
                    u.first_name,
                    u.last_name,
                    u.middle_name,
                    u.profile_picture,
                    u.email,
                    u.phone,
                    u.country,
                    u.country_code,
                    u.user_type,
                    u.company_name,
                    u.description,
                    u.password_hash,
                    u.theme,
                    cityName = lang == "ar" ? c.NameAr : c.NameEn,
                    CityId = u.city,
                    WasenderSessionId = acc.WasenderSessionId
                }
            )
            .AsNoTracking()
            .FirstOrDefaultAsync();

            if (user == null)
                return NotFound(new { message = "User not found" });

            return Ok(user);
        }

        [HttpGet("getSessionId")]
        public async Task<IActionResult> GetSessionId(int userId, int platformId = 1)
        {
            var sessionId = await _context.user_accounts
                .Where(ua => ua.UserId == userId && ua.PlatformId == platformId)
                .Select(ua => new { ua.WasenderSessionId,ua.AccountIdentifier})
                .FirstOrDefaultAsync();

            if (sessionId==null)
                return NotFound(new { message = "SessionId not found for this user/platform" });

            return Ok(new { WasenderSessionId = sessionId.WasenderSessionId , AccountIdentifier = sessionId.AccountIdentifier });
        }


        [HttpGet("getCityBy/{id}")]
        public async Task<IActionResult> getCityBy(int id)
        {
            var city = await _context.Cities
                .AsNoTracking() // قراءة فقط بدون تتبع التغييرات
                .FirstOrDefaultAsync(c => c.Id == id);

            if (city == null)
                return NotFound(new { message = "city not found" });

            return Ok(city); 
        }


        [HttpPut("update-theme/{id}")]
        public async Task<IActionResult> UpdateTheme(int id, [FromBody] UpdateProfileDto dto)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound(new { message = "User not found" });
            if (!string.IsNullOrEmpty(dto.Theme)) user.theme = dto.Theme;
            if (dto.Accept_Notifications.HasValue) user.accept_notifications = dto.Accept_Notifications.Value;
            if (!string.IsNullOrEmpty(dto.Language)) user.language = dto.Language;
            user.updated_at = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(new { message = "", user });
        }

        [HttpGet("conversations/{userId}")]
        public async Task<IActionResult> GetConversations(int userId)
        {
            var emptyConversations = await _context.Conversations
                .Where(c => c.UserId == userId && !c.conversation_messages.Any())
                .ToListAsync();

            if (emptyConversations.Any())
            {
                _context.Conversations.RemoveRange(emptyConversations);
                await _context.SaveChangesAsync();
            }

            var conversations = await _context.Conversations
                .Where(c => c.Status != "closed"
                            && c.UserId == userId
                            && c.conversation_messages.Any())
                .OrderByDescending(c => c.StartedAt)
                .Include(c => c.Agent)
                .Include(c => c.conversation_messages)
                .Select(c => new
                {
                    status = c.Status,
                    id = c.Id,
                    coDate = c.StartedAt.HasValue ? c.StartedAt.Value.ToString("dd/MM/yyyy") : "",
                    lastMessage = c.conversation_messages
                        .OrderByDescending(m => m.SentAt)
                        .Select(m => m.MessageText)
                        .FirstOrDefault(),
                    agentName = c.Agent != null
                        ? c.Agent.FirstName + " " + c.Agent.LastName
                        : "الدعم الفني",

                   
                    unreadCount = c.conversation_messages.Count(m => m.Sender == "support" && m.IsRead == false)
                })
                .ToListAsync();

            return Ok(conversations);
        }

        [HttpPost("conversations/{id}/mark-as-read")]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            var messages = await _context.conversation_messages
                .Where(m => m.ConversationId == id && !m.IsRead)
                .ToListAsync();

            foreach (var msg in messages)
            {
                msg.IsRead = true;
            }

            await _context.SaveChangesAsync();
            return Ok();
        }


        [HttpGet("conversations/{id}/get_messages")]
        public async Task<IActionResult> GetMessages(int id)
        {
            var messages = await _context.conversation_messages
                .Where(m => m.ConversationId == id)
                .OrderBy(m => m.SentAt)
                .ToListAsync();
            
            var unreadMessages = messages.Where(m => !m.IsRead).ToList();
            foreach (var msg in unreadMessages) msg.IsRead = true;
            await _context.SaveChangesAsync();

            var result = messages.Select(m => new {
                m.Id,
                m.Sender,
                m.MessageText,
                m.AttachmentUrl,
                m.SentAt
            }).ToList();

            return Ok(result);
        }


        [HttpPost("conversations/{id}/messages")]
        public async Task<IActionResult> SendMessage(int id, [FromBody] SendMessageDto dto)
        {
            var message = new conversation_messages
            {
                ConversationId = id,
                Sender = dto.Sender,
                MessageText = dto.MessageText,
                AttachmentUrl = dto.AttachmentUrl,
                SentAt = DateTime.UtcNow
            };

            _context.conversation_messages.Add(message);
            await _context.SaveChangesAsync();

            return Ok(message);
        }

        [HttpPost("conversations")]
        public async Task<IActionResult> CreateConversation([FromBody] CreateConversationDto dto)
        {
            var conversation = new Conversation
            {
                UserId = dto.UserId,
                AgentId = null, // 👈 لسه محدش استلم
                Status = "active",
                DurationMinutes = 30,
                StartedAt = DateTime.UtcNow
            };

            _context.Conversations.Add(conversation);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                conversation.Id,
                AgentName = "في انتظار موظف الدعم" // placeholder
            });
        }

        [HttpPost("{id}")]
        public async Task<IActionResult> DeactivateConversation(int id)
        {
            var conversation = await _context.Conversations.FindAsync(id);

            if (conversation == null)
                return NotFound();

            conversation.Status = "closed"; // 👈 بدل ما نحذفها
            await _context.SaveChangesAsync();

            return Ok(new { message = "Conversation closed successfully" });
        }

        [HttpPost("groups/request")]
        public async Task<IActionResult> AddGroupRequest([FromBody] GroupRequestDto model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);
            var entity = new GroupRequest
            {
                UserId = model.UserId,
                Platform = model.Platform,
                GroupName = model.GroupName,
                GroupLink = model.GroupLink,
                CountryId = model.CountryId,
                CategoryId = model.CategoryId,
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            };
            _context.group_requests.Add(entity);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, id = entity.Id });
        }

        [HttpGet("referral-link/{userId}")]
        public async Task<IActionResult> GetReferralLink(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            // لو عنده كود إحالة مسبقاً
            var referral = await _context.Referrals
                .FirstOrDefaultAsync(r => r.ReferrerId == userId);

            if (referral == null)
            {
                referral = new Referral
                {
                    ReferrerId = userId,
                    ReferralCode = Guid.NewGuid().ToString().Substring(0, 8).ToUpper()
                };
                _context.Referrals.Add(referral);
                await _context.SaveChangesAsync();
            }

            var link = $"https://myApp.com/referral?code={referral.ReferralCode}";
            return Ok(new { referralLink = link });
        }

        [HttpGet("notifications")]
        public async Task<IActionResult> GetNotifications()
        {
            var notifications = await _context.Notifications
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new {
                    id = n.Id,
                    title = n.Title,
                    message = n.Message,
                    createdAt = n.CreatedAt
                })
                .ToListAsync();

            return Ok(notifications);
        }

        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.Categories
                .Where(c => c.IsActive)
                .Select(c => new {
                    id = c.Id,
                    nameAr = c.NameAr,
                    nameEn = c.NameEn,
                    hint = c.hint
                })
                .ToListAsync();

            return Ok(categories);
        }

        [HttpGet("categories/{id}/GetSugestion")]
        public async Task<IActionResult> GetSugestion(int id)
        {
            var messages = await _context.marketing_messages
                .Where(m => m.CategoryId == id && m.IsActive)
                .Select(m => new {
                    id = m.Id,
                    messageAr = m.MessageAr,
                    messageEn = m.MessageEn
                })
                .ToListAsync();

            return Ok(messages);
        }

    }
}
