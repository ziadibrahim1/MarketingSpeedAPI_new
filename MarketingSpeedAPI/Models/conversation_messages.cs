using System.Text.Json.Serialization;

namespace MarketingSpeedAPI.Models
{
    public class conversation_messages
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }
        public string? Sender { get; set; }   // "user" or "support"
        public string? MessageText { get; set; }
        public string? AttachmentUrl { get; set; }
        public DateTime? SentAt { get; set; }
        [JsonIgnore]
        public virtual Conversation Conversation { get; set; }
    }
}
