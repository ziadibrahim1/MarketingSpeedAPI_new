using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketingSpeedAPI.Models
{
    public enum PackageStatus { Active, Inactive }
    public enum PackageAction { Create, Update, Archive, Freeze }

    [Table("packages")]
    public class Package
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("color")]
        public string color { get; set; } = "normal";
        [Column("Fcolor")]
        public string Fcolor { get; set; } = "0xFFFFFFFF";
        public string NameEn { get; set; } = string.Empty;

        [Column("DescriptionAr")]
        public string? DescriptionAr { get; set; }

        [Column("DescriptionEn")]
        public string? DescriptionEn { get; set; }

        [Column("price", TypeName = "decimal(10,2)")]
        public decimal Price { get; set; }

        [Column("discount", TypeName = "decimal(5,2)")]
        public decimal Discount { get; set; }

        [Column("duration_days")]
        public int DurationDays { get; set; }

        [Column("subscriber_count")]
        public int SubscriberCount { get; set; }

        [Column("package_history")]
        public string? PackageHistory { get; set; }

        [Column("status")]
        public string Status { get; set; } = "active";

        [Column("archived")]
        public bool Archived { get; set; } = false;

        [Column("scheduled_at")]
        public DateTime? ScheduledAt { get; set; }

        [Column("display_in_app")]
        public bool DisplayInApp { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [Column("ImageUrl")]
        public string? ImageUrl { get; set; }

        [Column("CategoryId")]
        public int? CategoryId { get; set; }

        // Navigation property
        [ForeignKey("CategoryId")]
        public PackageCategory? Category { get; set; }

        public ICollection<PackageFeature> Features { get; set; } = new List<PackageFeature>();
        public ICollection<PackageLog> Logs { get; set; } = new List<PackageLog>();
    }
    public class PackageCategory
    {
        public int Id { get; set; }
        public string? Name { get; set; } = string.Empty;
        public string? NameEn { get; set; } = string.Empty;

        // Navigation property
        public ICollection<Package> Packages { get; set; } = new List<Package>();
    }
    [Table("user_subscriptions")]
    public class UserSubscription
    {
        [Key]
        [Column("Id")]
        public int Id { get; set; }

        [ForeignKey("PackageId")]
        public Package Package { get; set; } = null!;

        [Column("UserId")]
        public int UserId { get; set; }
  

        [Column("PackageId")]
        public int PackageId { get; set; }

        [Column("Add_groups_limit")]
        public int? Add_groups_limit { get; set; }

        [Column("PlanName")]
        public string PlanName { get; set; } = string.Empty;

        [Column("Price")]
        public decimal Price { get; set; }

        [Column("StartDate")]
        public DateTime StartDate { get; set; }

        [Column("EndDate")]
        public DateTime EndDate { get; set; }

        [Column("PaymentStatus")]
        public string PaymentStatus { get; set; } = "pending";

        [Column("IsActive")]
        public bool IsActive { get; set; } = true;

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; }

        [Column("UpdatedAt")]
        public DateTime UpdatedAt { get; set; }

        public ICollection<SubscriptionUsage> Usage { get; set; } = new List<SubscriptionUsage>();
    }
}
