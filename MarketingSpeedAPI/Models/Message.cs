namespace MarketingSpeedAPI.Models
{
    public class EditMessageRequest
    {
        public string Message { get; set; } = string.Empty;
    }


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
        public bool toGroupMember { get; set; } = false;
        public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;


        // ✅ إضافات مهمة
        public string? ExternalMessageId { get; set; } // id اللي بيرجع من API
        public string? AccessToken { get; set; } // لو محتاج تخزن التوكن (اختياري)
    }
    public class SendMembersRequest
    {
        public ulong UserId { get; set; }
        public int PlatformId { get; set; } = 1; // افتراضي واتساب
        public string Message { get; set; } = string.Empty;
        public List<string> Recipients { get; set; } = new();
        public List<string>? ImageUrls { get; set; }
    }

    public class BlockedGroup
    {
        public int Id { get; set; }
        public string GroupId { get; set; } = "";
        public int UserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public class SendGroupsRequest
    {
        // قائمة الـ Group IDs لإرسال الرسائل إليها
        public List<string> GroupIds { get; set; } = new List<string>();

        // نص الرسالة الذي سيرفق مع آخر صورة
        public string Message { get; set; } = string.Empty;

        // قائمة روابط الصور لإرسالها كمرفقات
        public List<string> ImageUrls { get; set; } = new List<string>();
    }

    public class UserImage
    {
        public int Id { get; set; }
        public long UserId { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.Now;
    }


    public class MessageAttachment
    {
        public int Id { get; set; }
        public int MessageId { get; set; }
        public int ImageId { get; set; }

        public Message Message { get; set; } = null!;
        public UserImage UserImage { get; set; } = null!;
    }

    public class AttachmentDto
    {
        public string Type { get; set; } = ""; // "image" | "video" | "document"
        public string Url { get; set; } = "";  // أو Base64 لو عايز
        public string FileName { get; set; } = ""; // للمستندات
    }

    public class CreateGroupRequest
    {
        public string Name { get; set; } = "";
        public List<string> SourceGroupIds { get; set; } = new List<string>();
    }

}
