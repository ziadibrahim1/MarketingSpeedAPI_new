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
        public long Id { get; set; }

        [Required, MaxLength(255)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("price", TypeName = "decimal(10,2)")]
        public decimal Price { get; set; }

        [Column("discount", TypeName = "decimal(5,2)")]
        public decimal Discount { get; set; } = 0;

        [Column("duration_days")]
        public int DurationDays { get; set; }

        [Column("subscriber_count")]
        public int SubscriberCount { get; set; } = 0;

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

        public ICollection<PackageFeature> Features { get; set; } = new List<PackageFeature>();
        public ICollection<PackageLog> Logs { get; set; } = new List<PackageLog>();
    }

}
