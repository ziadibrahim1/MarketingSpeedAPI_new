namespace MarketingSpeedAPI.Models
{
    public class LoginRequest
    {
        public string email { get; set; } = string.Empty;
        public string password_hash { get; set; } = string.Empty;
    }
}
