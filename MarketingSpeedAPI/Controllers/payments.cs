using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RestSharp;
using System.Text;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly MoyasarSettings _settings;

        public PaymentsController(AppDbContext context, IOptions<MoyasarSettings> settings)
        {
            _context = context;
            _settings = settings.Value;
        }

        // ----------------------------------------------------
        // 1) CALCULATE PRICE AFTER COUPON
        // ----------------------------------------------------
        [HttpPost("calc")]
        public async Task<IActionResult> CalcPrice([FromBody] PaymentCalcRequest req)
        {
            var package = await _context.Packages
                .FirstOrDefaultAsync(p => p.Id == req.PackageId);

            if (package == null)
                return BadRequest(new { error = "Package not found" });

            decimal originalPrice = package.Price;
            decimal finalPrice = originalPrice;
            int? couponId = null;

            if (!string.IsNullOrWhiteSpace(req.Coupon))
            {
                var coupon = await _context.Coupons
                    .FirstOrDefaultAsync(c =>
                        c.Code == req.Coupon &&
                        c.IsActive &&
                        c.ExpiryDate >= DateTime.Now.Date);

                if (coupon != null)
                {
                    couponId = coupon.Id;

                    if (coupon.DiscountType == "percent")
                    {
                        decimal discount = originalPrice * (coupon.DiscountValue / 100);

                        if (coupon.MaxDiscount != null && discount > coupon.MaxDiscount)
                            discount = coupon.MaxDiscount.Value;

                        finalPrice = originalPrice - discount;
                    }
                    else if (coupon.DiscountType == "amount")
                    {
                        finalPrice = originalPrice - coupon.DiscountValue;
                    }

                    if (finalPrice < 1)
                        finalPrice = 1;
                }
            }

            return Ok(new
            {
                packageId = req.PackageId,
                originalPrice,
                finalPrice,
                couponId
            });
        }

        // ----------------------------------------------------
        // 2) VERIFY PAYMENT AND ACTIVATE PACKAGE
        // ----------------------------------------------------
        [HttpPost("verify")]
        public async Task<IActionResult> VerifyPayment([FromBody] VerifyPaymentRequest req)
        {
            try
            {
                // 1️⃣ استعلام حالة الدفع من ميسّر
                string secretKey = _settings.SecretKey; 
                var client = new RestClient($"https://api.moyasar.com/v1/payments/{req.PaymentId}");

                var request = new RestRequest("", Method.Get);

                string auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{secretKey}:"));
                request.AddHeader("Authorization", "Basic " + auth);

                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                    return BadRequest(new { message = "Failed to fetch payment details", detail = response.Content });

                var payment = JsonConvert.DeserializeObject<MoyasarPaymentResponse>(response.Content);

                // 2️⃣ التأكد من أن الدفع ناجح فعلياً
                if (payment.status != "paid")
                {
                    // حفظ محاولة دفع فاشلة
                    _context.Payment_Records.Add(new Payment_Records
                    {
                        PaymentId = req.PaymentId,
                        UserId = req.UserId,
                        PackageId = req.PackageId,
                        Amount = payment.amount / 100m,
                        Status = payment.status
                    });
                    await _context.SaveChangesAsync();

                    return BadRequest(new { message = "Payment not paid", payment.status });
                }

                // 3️⃣ تفعيل الاشتراك
                var package = await _context.Packages.FindAsync(req.PackageId);
                if (package == null)
                    return BadRequest(new { message = "Package not found" });

                var extraDays = 0;

                // البحث عن الكوبون
                Marketer marketer = null;

                if (!string.IsNullOrWhiteSpace(req.coupon))
                {
                    marketer = await _context.Marketers
                        .FirstOrDefaultAsync(m =>
                            m.PromoCode == req.coupon &&
                            !m.IsFrozen &&
                            !m.IsDeleted
                        );

                    if (marketer != null)
                    {
                        extraDays = 3;
                    }
                }

                var subscription = new UserSubscription
                {
                    UserId = req.UserId,
                    PackageId = req.PackageId,
                    PlanName = package.Name,
                    Price = package.Price,
                    StartDate = DateTime.Now,
                    EndDate = DateTime.Now.AddDays(package.DurationDays + extraDays),
                    PaymentStatus = "paid",
                    IsActive = true,
                    CreatedAt = DateTime.Now,
                };

                _context.UserSubscriptions.Add(subscription);

                // سجل الدفع
                _context.Payment_Records.Add(new Payment_Records
                {
                    PaymentId = req.PaymentId,
                    UserId = req.UserId,
                    PackageId = req.PackageId,
                    Amount = payment.amount / 100m,
                    Status = "paid"
                });

                // زيادة عدد المشتركين
                package.SubscriberCount += 1;

                await _context.SaveChangesAsync();

                // تحديث user.subscreption
                var user = await _context.Users.FindAsync(req.UserId);
                if (user != null)
                {
                    user.subscreption = subscription.Id;
                    await _context.SaveChangesAsync();
                }

                return Ok(new { message = "Subscription activated", subscriptionId = subscription.Id });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

    }
}
