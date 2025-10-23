namespace MarketingSpeedAPI.Models
{
    public class PackageDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? color { get; set; }
        public string? Fcolor { get; set; }
        public string? NameEn { get; set; }
        public string? CategoryName { get; set; }
        public string? CategoryNameEN { get; set; }
        public int? CategoryId { get; set; }
        public double Price { get; set; }
        public int DurationDays { get; set; }
        public double? Discount { get; set; }
        public List<FeatureDto>? Features { get; set; } = new List<FeatureDto>();
        public List<FeatureDto>? FeaturesEn { get; set; } = new List<FeatureDto>();
        public int SubscriberCount { get; set; }
        public string? ImageUrl { get; set; }
    }
    
    public class FeatureDto
    {
        public string feature { get; set; } = string.Empty;
        public string FeatureEn { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public int LimitCount { get; set; } = 0;
       

    }

    public class FeatureUsageDto
    {
        public string feature { get; set; } = "";
        public string FeatureEn { get; set; } = "";
        public int Limit { get; set; } = 0;        // الحد الأقصى من package_limits أو package_features
        public int Used { get; set; } = 0;         // الاستخدام الحالي من subscription_usage
    }

    public class UsageDto
    {
        public int Messages { get; set; }
        public int Media { get; set; }
        public string? LastUsage { get; set; } // ISO string
    }

    public class SubscriptionResultDto
    {
        public int Id { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int DaysLeft { get; set; }
        public PackageDto? Package { get; set; }
        public UsageDto Usage { get; set; } = new UsageDto();
    }

}
