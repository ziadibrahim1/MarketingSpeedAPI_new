namespace MarketingSpeedAPI.Models
{
    public class AppUser
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public string DeviceId { get; set; }
        public string? PromoCodeUsed { get; set; }
        public int? MarketerId { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsVerified { get; set; }
    }
    public class RegisterAppUserRequest
    {
        public string Email { get; set; }
        public string DeviceId { get; set; }
        public string? PromoCode { get; set; }
    }
}
