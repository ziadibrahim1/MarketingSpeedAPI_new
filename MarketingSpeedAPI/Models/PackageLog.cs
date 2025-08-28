using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketingSpeedAPI.Models
{
    [Table("package_logs")]
    public class PackageLog
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("PackageId")]
        public long PackageId { get; set; }

        [Column("UserId")]
        public long UserId { get; set; }

        [Column("action")]
        public string Action { get; set; } = "create";

        [Column("description")]
        public string? Description { get; set; }

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; }

        [ForeignKey("PackageId")]
        public Package? Package { get; set; }
    }
}
