namespace MarketingSpeedAPI.Models
{
    public class GroupRequestDto
    {
        public int UserId { get; set; }
        public string Platform { get; set; } = "";
        public string? GroupName { get; set; }
        public string GroupLink { get; set; } = "";
        public int? CountryId { get; set; }
        public int? CategoryId { get; set; }
    }
}
