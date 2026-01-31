using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/app/users")]
    public class AppUsersController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AppUsersController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterAppUserRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.DeviceId))
                return BadRequest(new { message = "Email and DeviceId are required" });

            // 1️⃣ منع التكرار
            var exists = await _context.app_users
                .AnyAsync(x => x.Email == req.Email || x.DeviceId == req.DeviceId);

            if (exists)
                return Conflict(new { message = "User already exists" });

            int? marketerId = null;
             
            var currUser = await _context.Users
                .FirstOrDefaultAsync(u => u.email == req.Email);
            // 2️⃣ التحقق من البروموكود
            if (!string.IsNullOrWhiteSpace(req.PromoCode))
            {
                var marketer = await _context.Marketers
                    .FirstOrDefaultAsync(m =>
                        m.PromoCode == req.PromoCode &&
                        !m.IsDeleted &&
                        !m.IsFrozen);

                if (marketer == null)
                    return BadRequest(new { message = "Invalid promo code" });

                marketerId = marketer.Id;

                // 3️⃣ زيادة نقطة للمسوق
                marketer.PointsAccumulated += 1;
            }

            // 4️⃣ إنشاء المستخدم
            var user = new AppUser
            {
                Email = req.Email,
                DeviceId = req.DeviceId,
                PromoCodeUsed = req.PromoCode,
                MarketerId = marketerId,
                CreatedAt = DateTime.Now,
                IsVerified = true
            };

            _context.app_users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "User registered successfully",
                marketerId
            });
        }
    }
}
