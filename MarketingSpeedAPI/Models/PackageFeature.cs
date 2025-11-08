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

        [ForeignKey("PackageId")]
        public Package Package { get; set; } = null!;

        [Column("feature")]
        public string? feature { get; set; }

        [Column("FeatureEn")]
        public string? FeatureEn { get; set; }

        [Column("Channel")] // whatsapp, telegram, facebook, instagram, sms, email, x, haraj ...
        public string Channel { get; set; } = string.Empty;

        [Column("ActionType")] // message, post, media, scheduled
        public string ActionType { get; set; } = "message";

        [Column("LimitCount")]
        public int LimitCount { get; set; } = 0;

        [Column("sendingLimit")]
        public int sendingLimit { get; set; } = 0;

        [Column("PlatformId")]
        public int PlatformId { get; set; }

        [Column("isMain")]
        public bool isMain { get; set; }=true;

        [Column("forMembers")]
        public bool forMembers { get; set; }=false;

        [Column("forCreatingGroups")]
        public bool forCreatingGroups { get; set; }=false;

        [Column("forGetingGruops")]
        public bool forGetingGruops { get; set; }=false;


        [Column("created_at")]
        public DateTime created_at { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime updated_at { get; set; } = DateTime.UtcNow;
    }

}
