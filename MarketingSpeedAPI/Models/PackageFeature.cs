using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketingSpeedAPI.Models
{
    [Table("package_features")]
    public class PackageFeature
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("package_id")]
        public int PackageId { get; set; }

        [Column("feature")]
        public string Feature { get; set; } = string.Empty;

        [Column("FeatureAr")]
        public string FeatureAr { get; set; } = string.Empty;

        [Column("FeatureEn")]
        public string FeatureEn { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [ForeignKey("PackageId")]
        public Package Package { get; set; } = null!;
    }
}
