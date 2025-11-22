namespace MarketingSpeedAPI.Models
{
    public class Payment_Records
    {
        public int Id { get; set; }
        public string PaymentId { get; set; }
        public int UserId { get; set; }
        public int PackageId { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } // paid / failed
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
    public class VerifyPaymentRequest
    {
        public string PaymentId { get; set; }
        public int UserId { get; set; }
        public int PackageId { get; set; }
    }

    public class MoyasarPaymentResponse
    {
        public string id { get; set; }
        public string status { get; set; }
        public int amount { get; set; }
        public string currency { get; set; }
    }

}
