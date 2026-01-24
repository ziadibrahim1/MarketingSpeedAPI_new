using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Dtos;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CouponsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CouponsController(AppDbContext context)
        {
            _context = context;
        }

        // ✅ إضافة كوبون جديد
        [HttpPost]
        public async Task<IActionResult> CreateCoupon([FromBody] CreateCouponDto dto)
        {
            if (await _context.Coupons.AnyAsync(c => c.Code == dto.Code))
                return BadRequest(new { error = "Coupon code already exists" });

            var coupon = new Coupon
            {
                Code = dto.Code.Trim().ToUpper(),
                DiscountType = dto.DiscountType,
                DiscountValue = dto.DiscountValue,
                MaxDiscount = dto.MaxDiscount,
                ExpiryDate = dto.ExpiryDate,
                IsActive = dto.IsActive
            };

            _context.Coupons.Add(coupon);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, id = coupon.Id });
        }

        // ✅ تعطيل/تفعيل كوبون
        [HttpPut("{id}/toggle")]
        public async Task<IActionResult> ToggleCoupon(int id)
        {
            var coupon = await _context.Coupons.FindAsync(id);
            if (coupon == null)
                return NotFound(new { error = "Coupon not found" });

            coupon.IsActive = !coupon.IsActive;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, coupon.IsActive });
        }

        // ✅ فحص كوبون (لللوحة أو للعميل قبل الدفع)
        // GET: /api/coupons/check?code=ABC123
        [HttpGet("check")]
        public async Task<IActionResult> CheckCoupon([FromQuery] string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest(new { error = "Code is required" });

            var coupon = await _context.Coupons
                .Where(c => c.Code == code.Trim().ToUpper())
                .FirstOrDefaultAsync();

            if (coupon == null)
                return NotFound(new { valid = false, reason = "Not found" });

            if (!coupon.IsActive)
                return Ok(new { valid = false, reason = "Inactive" });

            if (coupon.ExpiryDate < DateTime.Now.Date)
                return Ok(new { valid = false, reason = "Expired" });

            return Ok(new
            {
                valid = true,
                coupon = new CouponResponseDto
                {
                    Id = coupon.Id,
                    Code = coupon.Code,
                    DiscountType = coupon.DiscountType,
                    DiscountValue = coupon.DiscountValue,
                    MaxDiscount = coupon.MaxDiscount,
                    ExpiryDate = coupon.ExpiryDate,
                    IsActive = coupon.IsActive
                }
            });
        }

        // ✅ قائمة الكوبونات (للوحة الإدارة)
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var list = await _context.Coupons
                .OrderByDescending(c => c.Id)
                .Select(c => new CouponResponseDto
                {
                    Id = c.Id,
                    Code = c.Code,
                    DiscountType = c.DiscountType,
                    DiscountValue = c.DiscountValue,
                    MaxDiscount = c.MaxDiscount,
                    ExpiryDate = c.ExpiryDate,
                    IsActive = c.IsActive
                })
                .ToListAsync();

            return Ok(list);
        }
    }
}
