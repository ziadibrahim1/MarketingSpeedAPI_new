using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketingSpeedAPI.Models
{
    [Table("package_features")]
    public class PackageFeature
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("package_id")]
        public long PackageId { get; set; }

        [Required, MaxLength(255)]
        [Column("feature")]
        public string Feature { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [ForeignKey("PackageId")]
        public Package? Package { get; set; }
    }
}
