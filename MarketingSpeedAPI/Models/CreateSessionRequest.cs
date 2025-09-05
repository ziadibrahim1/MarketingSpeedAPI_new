namespace MarketingSpeedAPI.Models
{
    public class CreateSessionRequests
    {
        public int UserId { get; set; }
        public int PlatformId { get; set; }
        public string Name { get; set; }
        public string PhoneNumber { get; set; }
        public bool LogMessages { get; set; }
        public bool AccountProtection { get; set; }
    }


    public class ConnectRequest
    {
        public int UserId { get; set; }
        public int PlatformId { get; set; }
        public string AccountIdentifier { get; set; }
        public string DisplayName { get; set; }
    }
}
