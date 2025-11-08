using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketingSpeedAPI.Models
{
    public class SubscriptionUsage
    {
        [Key]
        public long Id { get; set; }

        public int UserId { get; set; }

        // ✅ مطابق للجدول
        public int SubscriptionId { get; set; }

        // ✅ مطابق للجدول
        public int PackageId { get; set; }

        // ✅ مطابق للجدول
        public int FeatureId { get; set; }

        public int UsedCount { get; set; }
        public int LimitCount { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public int RemainingCount { get; set; }

        public DateTime? LastUsedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // ✅ العلاقات (تحديد ForeignKey صريح)
        [ForeignKey("SubscriptionId")]
        public UserSubscription UserSubscription { get; set; }

        [ForeignKey("FeatureId")]
        public PackageFeature Feature { get; set; }

        [ForeignKey("UserId")]
        public User User { get; set; }

        [ForeignKey("PackageId")]
        public Package Package { get; set; }
    }
    public class UpdateUsageDto
    {
        public int used { get; set; }
        public string featureKey { get; set; }
    }
}
