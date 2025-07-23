namespace MarketingSpeedAPI.DTOs
{
    public class RegisterDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public required string Password_Hash { get; set; }
    }
}
