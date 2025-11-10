namespace MarketingSpeedAPI.Models
{
    public class PaymentRequest
    {
        public int UserId { get; set; }
        public int PackageId { get; set; }
        public decimal Amount { get; set; }
        public string? Coupon { get; set; }

        // لو بتتعامل بكروت مباشرة (optional)
        public string? CardName { get; set; }
        public string? CardNumber { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
        public int Cvc { get; set; }
    }
}
