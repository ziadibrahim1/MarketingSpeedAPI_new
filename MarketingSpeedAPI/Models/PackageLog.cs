using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketingSpeedAPI.Models
{
    [Table("package_logs")]
    public class PackageLog
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("PackageId")]
        public int PackageId { get; set; }

        [Column("UserId")]
        public int UserId { get; set; }

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
