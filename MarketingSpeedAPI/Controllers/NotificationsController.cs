using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _context;

    public NotificationsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserNotifications(int userId)
    {
        var notifications = await _context.user_notifications
            .Where(un => un.UserId == userId ||  un.UserId ==1 )
            .Include(un => un.Notification)
            .OrderByDescending(un => un.Notification.CreatedAt)
            .Select(un => new NotificationDto
            {
                Id = un.Notification.Id,
                Title = un.Notification.Title,
                Message = un.Notification.Message,
                CreatedAt = un.Notification.CreatedAt,
                IsRead = un.IsRead
            })
            .ToListAsync();

        return Ok(notifications);
    }

    [HttpPost]
    public async Task<IActionResult> CreateNotification([FromBody] CreateNotificationDto dto)
    {
        var notification = new Notification
        {
            Title = dto.Title,
            Message = dto.Message,
            TargetAudience = dto.TargetAudience,
            Destination = dto.Destination,
            ScheduleAt = dto.ScheduleAt,
            CreatedAt = DateTime.UtcNow
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        // نربط كل المستخدمين بالإشعار (أو حسب الفئة)
        var users = await _context.Users.ToListAsync();
        {
            _context.user_notifications.Add(new UserNotification
            {
                UserId = 1,
                NotificationId = notification.Id,
                IsRead = false
            });
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "Notification created successfully" });
    }

    [HttpPut("mark-as-read/{userId}/{notificationId}")]
    public async Task<IActionResult> MarkAsRead(int userId, int notificationId)
    {
        var userNotification = await _context.user_notifications
            .FirstOrDefaultAsync(un => un.UserId == userId && un.NotificationId == notificationId || un.UserId ==1 && un.NotificationId == notificationId);

        if (userNotification == null) return NotFound();

        userNotification.IsRead = true;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Notification marked as read" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteNotification(int id)
    {
        var notification = await _context.Notifications.FindAsync(id);
        if (notification == null) return NotFound();

        _context.Notifications.Remove(notification);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Notification deleted" });
    }

    // ✅ 5- Delete UserNotification (لو يوزر عايز يمسح اشعار من عنده فقط)
    [HttpDelete("user/{userId}/{notificationId}")]
    public async Task<IActionResult> DeleteUserNotification(int userId, int notificationId)
    {
        var userNotification = await _context.user_notifications
            .FirstOrDefaultAsync(un => un.UserId == userId && un.NotificationId == notificationId);

        if (userNotification == null) return NotFound();

        _context.user_notifications.Remove(userNotification);
        await _context.SaveChangesAsync();

        return Ok(new { message = "User notification deleted" });
    }
    // ✅ 6 - Get Unread Count
    [HttpGet("user/{userId}/unread-count")]
    public async Task<IActionResult> GetUnreadCount(int userId)
    {
        var count = await _context.user_notifications
            .Where(un => un.UserId == userId && !un.IsRead || un.UserId == 1 && !un.IsRead)
            .CountAsync();

        return Ok(new { unreadCount = count });
    }

}
