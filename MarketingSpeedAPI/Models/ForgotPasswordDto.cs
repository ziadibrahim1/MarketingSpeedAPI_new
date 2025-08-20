namespace MarketingSpeedAPI.Models
{
    public class ForgotPasswordDto
    {
        public string Email { get; set; } = null!;
    }

    public class ResetPasswordDto
    {
        public string Email { get; set; } = null!;
        public string? Verification_Code { get; set; } = null!;
        public string New_Password { get; set; } = null!;
    }

}
