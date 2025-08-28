namespace MarketingSpeedAPI.Models
{
    public class PackageDto
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public double Price { get; set; }
        public int DurationDays { get; set; }
        public double? Discount { get; set; }
        public List<string> Features { get; set; }
        public int SubscriberCount { get; set; }
    }

}
