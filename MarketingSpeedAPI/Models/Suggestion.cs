namespace MarketingSpeedAPI.Models
{
    public class Suggestion
    {
        public int Id { get; set; }
        public string suggestionAr { get; set; }
        public string suggestionEn { get; set; }
        public bool isStarred { get; set; } = false;
        public DateTime createdAt { get; set; }
        public DateTime updatedAt { get; set; }
        public int UserId { get; set; }
        public ICollection<suggestion_replies> Replies { get; set; }
    }

    public class suggestion_replies
    {
        public int Id { get; set; }
        public int SuggestionId { get; set; }
        public int UserId { get; set; }
        public string ReplyText { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Suggestion Suggestion { get; set; }
    }
}
