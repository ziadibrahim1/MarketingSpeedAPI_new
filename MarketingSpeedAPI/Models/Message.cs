namespace MarketingSpeedAPI.Models
{
    public class Message
    {
        public int Id { get; set; }
        public int PlatformId { get; set; }
        public long UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? Targets { get; set; } // JSON string of recipients
        public string? Suggestions { get; set; } // JSON string
        public string? Attachments { get; set; } // JSON array
        public bool IsScheduled { get; set; } = false;
        public DateTime? ScheduledTime { get; set; }
        public string Status { get; set; } = "pending"; // pending | sent | failed
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SentAt { get; set; }

        public List<MessageLog> Logs { get; set; } = new();
    }

    public class MessageLog
    {
        public int Id { get; set; }
        public int MessageId { get; set; }
        public Message Message { get; set; } = null!;
        public string Recipient { get; set; } = string.Empty;
        public int PlatformId { get; set; }
        public string Status { get; set; } = "pending"; // pending | sent | failed
        public string? ErrorMessage { get; set; }
        public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
    }
    public class SendMessageRequests
    {
        public int PlatformId { get; set; }
        public long UserId { get; set; }
        public string Type { get; set; } = string.Empty; // نوع الإرسال: "whatsapp_groups", "group_members", "private_chats", "contacts"
        public List<string> Recipients { get; set; } = new(); // قائمة المستلمين
        public string Message { get; set; } = string.Empty; // نص الرسالة
        public string? Title { get; set; }
        public string? Suggestions { get; set; } // نصائح أو اقتراحات اختيارية
        public string? Attachments { get; set; } // JSON string للملفات المرفقة
    }


}
