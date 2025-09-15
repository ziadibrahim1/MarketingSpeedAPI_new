namespace MarketingSpeedAPI.Models
{
    public class Category
    {
        public int Id { get; set; }

        public string NameAr { get; set; }   // اسم المجال بالعربية
        public string NameEn { get; set; }   // اسم المجال بالإنجليزية
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

}
