namespace MarketingSpeedAPI.Models
{
    public class PaymentVerifyRequest
    {
        public string PaymentId { get; set; }
        public int UserId { get; set; }
        public int PackageId { get; set; }
    }
    public class PaymentCalcRequest
    {
        public int UserId { get; set; }
        public int PackageId { get; set; }
        public string? Coupon { get; set; }
    }
}
