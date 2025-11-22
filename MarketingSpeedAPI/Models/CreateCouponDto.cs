namespace MarketingSpeedAPI.Dtos
{
    public class CreateCouponDto
    {
        public string Code { get; set; }
        public string DiscountType { get; set; } // "percent" أو "amount"
        public decimal DiscountValue { get; set; }
        public decimal? MaxDiscount { get; set; }
        public DateTime ExpiryDate { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class CouponResponseDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string DiscountType { get; set; }
        public decimal DiscountValue { get; set; }
        public decimal? MaxDiscount { get; set; }
        public DateTime ExpiryDate { get; set; }
        public bool IsActive { get; set; }
    }
}
