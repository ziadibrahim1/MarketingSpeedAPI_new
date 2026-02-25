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

            var exists = await _context.app_users
                .AnyAsync(x => x.Email == req.Email || x.DeviceId == req.DeviceId);

            if (exists)
                return Conflict(new { message = "User already exists" });

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                int? marketerId = null;
                decimal commission = 0;
                decimal Subcommission = 0;

                var currUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.email == req.Email);

                UserSubscription lastSub = null;

                if (currUser != null)
                {
                    lastSub = await _context.UserSubscriptions
                        .OrderByDescending(s => s.StartDate)
                        .FirstOrDefaultAsync(s => s.UserId == currUser.id);
                }

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
                    var supervisor = await _context.Supervisors
                        .FirstOrDefaultAsync(s => s.Id == marketer.SupervisorId);
                    if (lastSub != null)
                    {
                        commission = (decimal)(lastSub.Price * (marketer.PointPrice / 100m));
                        Subcommission = lastSub.Price * (supervisor.PointPrice / 100m);
                    }
                    marketer.totalDueAmount = (marketer.totalDueAmount ?? 0) + commission;
                    marketer.PointsAccumulated += 1;

                    
                    if (supervisor != null)
                        {
                        supervisor.AmountDue ??= 0m;
                        supervisor.AmountDue += Subcommission;
                    }

                }

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

                await transaction.CommitAsync();

                return Ok(new
                {
                    message = "User registered successfully",
                    marketerId,
                    commission
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
