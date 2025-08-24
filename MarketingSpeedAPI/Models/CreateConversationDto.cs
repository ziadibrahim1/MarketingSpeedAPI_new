namespace MarketingSpeedAPI.Models
{
    public class CreateConversationDto
    {
        public int UserId { get; set; }
        public int? AgentId { get; set; } // ممكن تسيبه null وتخصص Agent لاحقاً
    }
}
