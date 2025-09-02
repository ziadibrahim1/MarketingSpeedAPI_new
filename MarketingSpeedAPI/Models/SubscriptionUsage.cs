using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketingSpeedAPI.Models
{
    [Table("subscription_usage")]
    public class SubscriptionUsage
    {
        [Key]
        [Column("Id")]
        public int Id { get; set; }

        [Column("UserId")]
        public int UserId { get; set; }

        [Column("SubscriptionId")]
        public int SubscriptionId { get; set; }

        [ForeignKey("SubscriptionId")]
        public UserSubscription Subscription { get; set; } = null!;

        [Column("Channel")] // whatsapp, telegram, facebook, etc.
        public string Channel { get; set; } = string.Empty;

        [Column("SubChannel")] // whatsapp, telegram, facebook, etc.
        public string SubChannel { get; set; } = string.Empty;

        [Column("ActionType")] // message, post, media, scheduled
        public string ActionType { get; set; } = string.Empty;

        [Column("MessageCount")]
        public int MessageCount { get; set; } = 0;
        public int MediaCount { get; set; } = 0;

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

}
