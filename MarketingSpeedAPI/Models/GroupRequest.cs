namespace MarketingSpeedAPI.Models
{
    public class GroupRequest
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Platform { get; set; } = null!;
        public string? GroupName { get; set; }
        public string GroupLink { get; set; } = null!;
        public int? CountryId { get; set; }
        public int? CategoryId { get; set; }
        public string Status { get; set; } = "pending";
        public string? AdminNote { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ReviewedAt { get; set; }
    }
}
