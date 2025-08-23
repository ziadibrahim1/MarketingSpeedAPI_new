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
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var lang = (dto.language ?? "en").ToLower();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.email == dto.email);

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

                // إرسال البريد أولاً
                var emailSent = await _emailService.SendVerificationEmailAsync(user.email, code);
                if (!emailSent)
                    return StatusCode(500, new { error = "فشل إرسال بريد التحقق" });

                _context.Users.Add(user);
                await _context.SaveChangesAsync(); // هنا بيتولد الـ id (لو auto increment)

                return Ok(new
                {
                    message = "تم التسجيل. تم إرسال رمز التحقق إلى بريدك الإلكتروني.",
                    userId = user.id   // <<< رجع الـ id للموبايل
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

            // ✅ نحدث بس الحقول اللي جت من الـ DTO
            if (!string.IsNullOrEmpty(dto.First_Name)) user.first_name = dto.First_Name;
            if (!string.IsNullOrEmpty(dto.Middle_Name)) user.middle_name = dto.Middle_Name;
            if (!string.IsNullOrEmpty(dto.Last_Name)) user.last_name = dto.Last_Name;
            if (!string.IsNullOrEmpty(dto.Country_Code)) user.country_code = dto.Country_Code;
            if (!string.IsNullOrEmpty(dto.Phone)) user.phone = dto.Phone;
            if (!string.IsNullOrEmpty(dto.Country)) user.country = dto.Country;
            if (dto.City.HasValue) user.city = dto.City;
            if (!string.IsNullOrEmpty(dto.User_Type)) user.user_type = dto.User_Type;
            if (!string.IsNullOrEmpty(dto.Company_Name)) user.company_name = dto.Company_Name;
            if (!string.IsNullOrEmpty(dto.Description)) user.description = dto.Description;
            if (!string.IsNullOrEmpty(dto.Profile_Picture)) user.profile_picture = dto.Profile_Picture;
            if (dto.Accept_Notifications.HasValue) user.accept_notifications = dto.Accept_Notifications.Value;
            if (dto.Accept_Terms.HasValue) user.accept_terms = dto.Accept_Terms.Value;
            if (!string.IsNullOrEmpty(dto.Language)) user.language = dto.Language;
            if (!string.IsNullOrEmpty(dto.Theme)) user.theme = dto.Theme;
            if (dto.last_seen.HasValue) user.last_seen = dto.last_seen;

            user.updated_at = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Profile updated successfully", user });
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

            return Ok(new { message = "تم التحقق من البريد الإلكتروني بنجاح" });
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

            var countries = await _context.Countries
                .Where(c => c.IsActive)
                .Select(c => new
                {
                    Id = c.Id,
                    Name = lang == "ar" ? c.NameAr : c.NameEn,
                    c.IsoCode,
                    c.PhoneCode
                })
                .ToListAsync();

            return Ok(countries);
        }

        [HttpGet("GetCities")]
        public async Task<IActionResult> GetCities(int countryId, string lang = "ar")
        {
            var cities = await _context.Cities
                .Where(c => c.CountryId == countryId && c.IsActive)
                .Select(c => new {
                    id = c.Id,
                    name = lang.ToLower() == "ar" ? c.NameAr : c.NameEn
                }).ToListAsync();

            return Ok(cities);
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

            await _context.SaveChangesAsync();

            // هنا ترسل الكود على الإيميل (SMTP أو أي خدمة Mail)
            // مثال: EmailService.Send(user.email, "Reset Code", $"Your code is {code}");

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
        public async Task<IActionResult> GetUserById(int id,string lang)
        {
            var user = await (
                from u in _context.Users
                join c in _context.Cities
                    on u.city equals c.Id into cityGroup
                from c in cityGroup.DefaultIfEmpty()   // 👈 Left Join
                where u.id == id
                select new
                {
                    u.id,
                    u.first_name,
                    u.last_name,
                    u.middle_name,
                    u.email,
                    u.phone,
                    u.country,
                    u.country_code,
                    u.user_type,
                    u.company_name,
                    u.description,
                    u.password_hash,
                    u.theme,
                    cityName =lang == "ar"? c.NameAr :c.NameEn,   
                    CityId = u.city
                }
            )
            .AsNoTracking()
            .FirstOrDefaultAsync();

            if (user == null)
                return NotFound(new { message = "User not found" });

            return Ok(user);
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

    }
}
