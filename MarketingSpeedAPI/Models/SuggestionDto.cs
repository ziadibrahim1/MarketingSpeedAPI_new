namespace MarketingSpeedAPI.Models
{
    public class SuggestionDto
    {
        public int Id { get; set; }
        public string SuggestionAr { get; set; }
        public string SuggestionEn { get; set; }
        public bool IsStarred { get; set; } = false;
        public int UserId { get; set; }
        public ICollection<SuggestionReplyDto>? RepliesDto { get; set; }
    }

    public class SuggestionReplyDto
    {
        public int Id { get; set; }
        public string ReplyText { get; set; }
        public int UserId { get; set; }
    }
}
