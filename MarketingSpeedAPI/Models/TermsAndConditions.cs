namespace MarketingSpeedAPI.Models
{
    public class TermsAndConditions
    {
        public int Id { get; set; }
        public string ContentAr { get; set; } = string.Empty;
        public string ContentEn { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
