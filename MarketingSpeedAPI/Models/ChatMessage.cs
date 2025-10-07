namespace MarketingSpeedAPI.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }                 
        public string? MessageId { get; set; }       
        public string UserPhone { get; set; }      
        public string Text { get; set; }          
        public string reciverNumber { get; set; }          
        public bool IsSentByMe { get; set; }     
        public bool IsRaeded { get; set; }     
        public DateTime Timestamp { get; set; }
        public string ContactName { get; set; }
        public string ProfileImageUrl { get; set; }

        public int? SessionId { get; set; }
    }
}
