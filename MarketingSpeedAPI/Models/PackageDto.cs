namespace MarketingSpeedAPI.Models
{
    public class PackageDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? NameEn { get; set; }
        public string? CategoryName { get; set; }
        public string? CategoryNameEN { get; set; }
        public int? CategoryId { get; set; }
        public double Price { get; set; }
        public int DurationDays { get; set; }
        public double? Discount { get; set; }
        public List<string>? Features { get; set; }
        public List<string>? FeaturesEn { get; set; }
        public int SubscriberCount { get; set; }
        public string? ImageUrl { get; set; }
    }

}
