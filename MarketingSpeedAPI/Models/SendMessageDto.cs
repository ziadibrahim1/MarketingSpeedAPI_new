namespace MarketingSpeedAPI.Models
{
    public class DeleteMessagesRequest
    {
        public List<string> Bodies { get; set; } = new();
    }
    public class AccountConnectionCache
    {
        public int UserId { get; set; }
        public bool IsConnected { get; set; }
        public DateTime LastChecked { get; set; }
    }

    public class SendMessageDto
    {
        public string Sender { get; set; }   // "user" أو "support"
        public string? MessageText { get; set; }
        public string? AttachmentUrl { get; set; }
    }
}
