namespace MarketingSpeedAPI.Models
{
    public class SendMessageDto
    {
        public string Sender { get; set; }   // "user" أو "support"
        public string? MessageText { get; set; }
        public string? AttachmentUrl { get; set; }
    }
}
