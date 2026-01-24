using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketingSpeedAPI.Models
{
    [Table("system_alerts")]
    public class SystemAlert
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("title")]
        [MaxLength(255)]
        public string? Title { get; set; }

        [Column("message")]
        public string? Message { get; set; }

        [Column("buttonText")]
        [MaxLength(100)]
        public string? ButtonText { get; set; }

        [Column("actionUrl")]
        public string? ActionUrl { get; set; }

        [Column("isMandatory")]
        public bool IsMandatory { get; set; }

        [Column("showOnce")]
        public bool ShowOnce { get; set; }

        [Column("minAppVersion")]
        [MaxLength(20)]
        public string? MinAppVersion { get; set; }

        [Column("latestVersion")]
        [MaxLength(20)]
        public string? LatestVersion { get; set; }

        [Column("platform")]
        [MaxLength(20)]
        public string? Platform { get; set; } = "android";

        [Column("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
