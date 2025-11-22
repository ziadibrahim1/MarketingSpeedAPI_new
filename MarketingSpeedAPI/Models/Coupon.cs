namespace MarketingSpeedAPI.Models
{
    public class Coupon
    {
        public int Id { get; set; }

        public string Code { get; set; }

        // "percent" أو "amount"
        public string DiscountType { get; set; }

        public decimal DiscountValue { get; set; }

        public decimal? MaxDiscount { get; set; }

        public DateTime ExpiryDate { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
