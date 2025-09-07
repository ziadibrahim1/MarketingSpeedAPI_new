namespace MarketingSpeedAPI.Models
{
    public class TelegramOptions
    {
        public int ApiId { get; set; }
        public string ApiHash { get; set; }
        public string BaseDataDir { get; set; } = "tdlib_data";
    }
}
