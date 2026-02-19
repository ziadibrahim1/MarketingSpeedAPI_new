using MarketingSpeedAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketingSpeedAPI.Models
{
    public class TelegramSessionService
    {
        private readonly AppDbContext _db;

        public TelegramSessionService(AppDbContext db)
        {
            _db = db;
        }

        public async Task SaveSessionAsync(long userId, string phone, long telegramUserId, string? username,string? firstName,bool is2Fa)
        {
            var sessions = await _db.TelegramSessions
                .Where(x => x.UserId == userId && x.IsActive)
                .ToListAsync();

            foreach (var s in sessions)
                s.IsActive = false;

            _db.TelegramSessions.Add(new TelegramSession
            {
                UserId = userId,
                PhoneNumber = phone,
                TelegramUserId = telegramUserId,
                Username = username,
                FirstName = firstName,
                Is2Fa = is2Fa,
                IsActive = true,
                SessionFile = $"{userId}.session",
                LastLoginAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }
        public async Task<bool> HasActiveSessionAsync(long userId)
        {
            return await _db.TelegramSessions
                            .AnyAsync(s => s.UserId == userId && s.IsActive);
        }

        public async Task MarkSessionInactiveAsync(long userId)
        {
            var session = await _db.TelegramSessions
                                   .FirstOrDefaultAsync(x => x.UserId == userId);

            if (session != null)
            {
                session.IsActive = false;
                await _db.SaveChangesAsync();
            }
        }

        public async Task DeactivateSessionsAsync(long userId)
        {
            var sessions = await _db.TelegramSessions
                .Where(x => x.UserId == userId && x.IsActive)
                .ToListAsync();

            foreach (var s in sessions)
                s.IsActive = false;

            await _db.SaveChangesAsync();
        }
    }


}
