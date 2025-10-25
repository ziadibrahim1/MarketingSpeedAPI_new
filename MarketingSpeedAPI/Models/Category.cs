namespace MarketingSpeedAPI.Models
{
    public class Category
    {
        public int Id { get; set; }

        public string NameAr { get; set; }   
        public string NameEn { get; set; }
        public string? hint { get; set; }
        public string? Description { get; set; }  // وصف المجال (اختياري)
        public bool IsActive { get; set; } = true;  // هل المجال متاح
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<MarketingMessage> MarketingMessages { get; set; }
    }
    public class MarketingMessage
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string MessageAr { get; set; }
        public string? MessageEn { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Category Category { get; set; }
    }
    public class GroupSubscription
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string GroupId { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "active";
        public DateTime? LastBatchTime { get; internal set; }
    }
    public class AddGroupMembersRequest
    {
        public string GroupId { get; set; }
        public List<string> Members { get; set; } = new();
    }

}
