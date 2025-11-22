namespace MarketingSpeedAPI.Models
{
    public class PaymentRequest
    {
        public int UserId { get; set; }
        public int PackageId { get; set; }
        public string? Coupon { get; set; }
    }
}
