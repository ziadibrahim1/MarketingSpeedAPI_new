namespace MarketingSpeedAPI.Models
{
    public class Notification
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string TargetAudience { get; set; } = "all";

        public DateTime? ScheduleAt { get; set; }

        public string Destination { get; set; } = "in_app";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // Navigation property (عشان المرفقات)
        public ICollection<NotificationAttachment>? Attachments { get; set; }
    }

    public class NotificationAttachment
    {
        public int Id { get; set; }

        public int NotificationId { get; set; }

        public string FileType { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // علاقة مع Notification
        public Notification Notification { get; set; }
    }
    // للإشعار الأساسي
    public class NotificationDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string body { get; set; }
        public DateTime dateTime { get; set; }
        public bool IsRead { get; set; }   // حالة القراءة الخاصة باليوزر
    }

    // لإنشاء إشعار جديد
    public class CreateNotificationDto
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public string TargetAudience { get; set; } = "all";  // optional
        public string Destination { get; set; } = "in_app";  // optional
        public DateTime? ScheduleAt { get; set; }            // optional
    }

}
