using MarketingSpeedAPI.Controllers;
using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketingSpeedAPI.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    public class TelegramController : ControllerBase
    {
        private readonly TelegramClientManager _tg;
        private readonly AppDbContext _db;

        public TelegramController(TelegramClientManager tg, AppDbContext db)
        {
            _tg = tg;
            _db = db;
        }

        [HttpPost("telelogin")]
        public async Task<IActionResult> StartLogin([FromBody] StartLoginRequest req)
        {
            var result = await _tg.StartLoginAsync(req.UserId, req.PhoneNumber);
            await Task.Yield();

            var acc = await _db.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == req.UserId && a.PlatformId == req.PlatformId);
            await Task.Yield();
            if (acc == null)
            {
                acc = new UserAccount
                {
                    UserId = req.UserId,
                    PlatformId = req.PlatformId,
                    AccountIdentifier = req.PhoneNumber,
                    DisplayName = req.DisplayName,
                    Status = "disconnected",
                    LastActivity = DateTime.UtcNow
                };
                _db.user_accounts.Add(acc);
            }
            else
            {
                acc.AccountIdentifier = req.PhoneNumber;
                acc.DisplayName = req.DisplayName;
                acc.Status = "disconnected";
                acc.LastActivity = DateTime.UtcNow;
            }
            await Task.Yield();
            await _db.SaveChangesAsync();
            return Ok(new { success = true, status = "wait_code", message = "Code sent" });
        }



        [HttpPost("confirm-code")]
        public async Task<IActionResult> ConfirmCode([FromBody] ConfirmCodeRequest req)
        {
            var result = await _tg.ConfirmCodeAsync(req.UserId, req.Code);
            await Task.Yield();
            var acc = await _db.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == req.UserId && a.PlatformId == req.PlatformId);

            if (result == "need_2fa_password" && acc != null)
            {
                acc.Status = "disconnected";
                acc.LastActivity = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return Ok(new { success = true, status = "disconnected" });
            }

            if (result == "connected" && acc != null)
            {
                acc.Status = "connected";
                acc.ConnectedAt = DateTime.UtcNow;
                acc.LastActivity = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return Ok(new { success = true, status = result });
        }



        [HttpPost("confirm-password")]
        public async Task<IActionResult> ConfirmPassword([FromBody] ConfirmPasswordRequest req)
        {
            var result = await _tg.ConfirmPasswordAsync(req.UserId, req.Password);

            var acc = await _db.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == req.UserId && a.PlatformId == req.PlatformId);

            if (acc != null)
            {
                acc.Status = "connected";
                acc.ConnectedAt = DateTime.UtcNow;
                acc.LastActivity = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return Ok(new { success = true, status = result });
        }

        [HttpGet("status/{userId:long}")]
        public IActionResult Status(long userId)
        {
            var status =  _tg.GetStatusAsync(userId);
            return Ok(new { success = true, status });
        }


        [HttpGet("check-connection/{userId:long}")]
        public async Task<IActionResult> CheckConnection(long userId)
        {
            var status = await _tg.GetStatusAsync(userId);

            var acc = await _db.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == userId && a.PlatformId == 2);

            if (status != "connected" && acc?.Status == "connected")
            {
                acc.Status = "disconnected";
                await _db.SaveChangesAsync();
            }

            return Ok(new { success = true, status });
        }


        [HttpPost("logout/{userId:long}")]
        public async Task<IActionResult> Logout(long userId)
        {
            await _tg.LogoutAsync(userId);

            var accs = _db.user_accounts.Where(a => a.UserId == userId && a.PlatformId == 2);
            await foreach (var acc in accs.AsAsyncEnumerable())
            {
                acc.Status = "disconnected";
                acc.LastActivity = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();

            return Ok(new { success = true, message = "logged_out" });
        }
    }
}
