namespace MarketingSpeedAPI.Models
{
    public class BlockedChat
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public string Phone { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
