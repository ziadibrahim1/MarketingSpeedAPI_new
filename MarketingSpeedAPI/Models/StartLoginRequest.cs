namespace MarketingSpeedAPI.Models
{
    public class StartLoginRequest
    {
        public long UserId { get; set; }
        public int PlatformId { get; set; }
        public string PhoneNumber { get; set; }
        public string DisplayName { get; set; }
    }

    public class ConfirmCodeRequest
    {
        public long UserId { get; set; }
        public int PlatformId { get; set; }
        public string Code { get; set; }
    }

    public class ConfirmPasswordRequest
    {
        public long UserId { get; set; }
        public int PlatformId { get; set; }
        public string Password { get; set; }
    }
}
