
namespace MarketingSpeedAPI.Models

{
    public class Conversation
    {
        public int Id { get; set; }
        public int? UserId { get; set; }
        public int? AgentId { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public string? Status { get; set; }
        public int? DurationMinutes { get; set; }
        public List<MessageDto> Messages { get; set; }
        public User User { get; set; }

        // 👈 العلاقة مع موظف الدعم
        public support_agents Agent { get; set; }

        public ICollection<conversation_messages> conversation_messages { get; set; }
    }

    public class MessageDto
    {
        public int Id { get; set; }
        public string Sender { get; set; }
        public string MessageText { get; set; }
        public DateTime? SentAt { get; set; }
    }

}

