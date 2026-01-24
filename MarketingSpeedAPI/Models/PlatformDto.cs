namespace MarketingSpeedAPI.Models
{
    public class PlatformDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string PlatformType { get; set; }
        public string Status { get; set; } // active / inactive
        public string? StatusMessage { get; set; }
    }
    public class PlatformStatusDto
    {
        public string PlatformName { get; set; }
        public string Status { get; set; } // active / inactive
        public string? StatusMessage { get; set; }
    }

}
