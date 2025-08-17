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
            var lang = (dto.Language ?? "en").ToLower();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

            if (user == null)
            {
                var msg = lang == "ar" ? "البريد الإلكتروني غير موجود" : "Email not found";
                return Unauthorized(new { message = msg });
            }

            var hashedPassword = ComputeSha256Hash(dto.Password);
            Console.WriteLine($"Hashed: {hashedPassword}");

            if (user.PasswordHash != hashedPassword)
            {
                var msg = lang == "ar" ? "كلمة المرور غير صحيحة" : "Incorrect password";
                return Unauthorized(new { message = msg });
            }

            if (!user.IsEmailVerified)
            {
                var msg = lang == "ar" ? "الحساب لم يتم تفعيله بعد" : "Account not verified yet";
                return Unauthorized(new { message = msg });
            }

            // تحديث آخر ظهور
            user.LastSeen = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var successMsg = lang == "ar" ? "تم تسجيل الدخول بنجاح" : "Login successful";

            return Ok(new
            {
                message = successMsg,
                user = new
                {
                    user.Id,
                    user.Email,
                    user.FirstName,
                    user.MiddleName,
                    user.LastName,
                    user.UserType,
                    user.CompanyName,
                    user.ProfilePicture,
                    user.Language,
                    user.Theme,
                    user.Status,
                    user.LastSeen
                }
            });
        }


        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            try
            {
                if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
                    return BadRequest(new { error = "Email already exists" });

                if (string.IsNullOrWhiteSpace(dto.Password))
                    return BadRequest(new { error = "كلمة المرور مطلوبة" });

                // توليد كود عشوائي من 6 أرقام
                string code = _random.Next(100000, 999999).ToString();

                var user = new User
                {
                    FirstName = dto.FirstName,
                    MiddleName = dto.MiddleName,
                    LastName = dto.LastName,
                    Email = dto.Email,
                    CountryCode = dto.CountryCode,
                    Phone = dto.Phone,
                    Country = dto.Country,
                    City = dto.City,
                    UserType = dto.UserType,
                    CompanyName = dto.CompanyName,
                    Description = dto.Description,
                    Language = dto.Language ?? "ar",
                    Theme = dto.Theme ?? "light",
                    Status = "active",
                    AcceptNotifications = dto.AcceptNotifications,
                    AcceptTerms = dto.AcceptTerms,
                    PasswordHash = ComputeSha256Hash(dto.Password),
                    VerificationCode = code,
                    VerificationCodeExpiresAt = DateTime.UtcNow.AddMinutes(2),
                    CreatedAt = DateTime.UtcNow,
                    IsEmailVerified = false
                };

                // إرسال البريد أولاً
                var emailSent = await _emailService.SendVerificationEmailAsync(user.Email, code);
                if (!emailSent)
                    return StatusCode(500, new { error = "فشل إرسال بريد التحقق" });

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                return Ok(new { message = "تم التسجيل. تم إرسال رمز التحقق إلى بريدك الإلكتروني." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }


        [HttpPost("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyCodeDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null)
                return NotFound(new { error = "المستخدم غير موجود" });

            if (user.VerificationCode != dto.Code)
                return BadRequest(new { error = "رمز التحقق غير صحيح" });

            if (user.VerificationCodeExpiresAt < DateTime.UtcNow)
                return BadRequest(new { error = "انتهت صلاحية رمز التحقق" });

            // تم التحقق بنجاح
            user.IsEmailVerified = true;
            user.VerificationCode = null;
            await _context.SaveChangesAsync();

            return Ok(new { message = "تم التحقق من البريد الإلكتروني بنجاح" });
        }

        [HttpPost("resend-code")]
        public async Task<IActionResult> ResendVerificationCode([FromBody] EmailDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null)
                return NotFound(new { error = "المستخدم غير موجود" });

            if (user.IsEmailVerified)
                return BadRequest(new { error = "البريد الإلكتروني تم التحقق منه بالفعل" });

            string code = _random.Next(100000, 999999).ToString();
            user.VerificationCode = code;
            user.VerificationCodeExpiresAt = DateTime.UtcNow.AddMinutes(2);

            await _emailService.SendVerificationEmailAsync(user.Email, code);
            await _context.SaveChangesAsync();

            return Ok(new { message = "تم إرسال كود تحقق جديد إلى البريد الإلكتروني" });
        }

        [HttpGet("GetCountries")]
        public async Task<IActionResult> GetCountries(string lang = "en")
        {
            lang = lang.ToLower();
            var countries = await _context.CountriesAndCities
                .GroupBy(c => lang == "ar" ? c.CountryNameAr : c.CountryNameEn)
                .Select(g => new
                {
                    Country = g.Key,
                    Cities = g.Select(c => lang == "ar" ? c.CityNameAr : c.CityNameEn).Distinct().ToList()
                })
                .ToListAsync();

            return Ok(countries);
        }

        [HttpGet("GetTerms")]
        public async Task<IActionResult> GetTerms(string lang = "en")
        {
            lang = lang.ToLower();
            var terms = await _context.TermsAndConditions
                .FirstOrDefaultAsync(t => t.Language == lang);

            return Ok(new
            {
                content = terms?.Content ?? ""
            });
        }

    }
}
