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

        [Column("Channel")]
        public string Channel { get; set; } = null!;

        [Column("ActionType")]
        public string ActionType { get; set; } = null!;

        [Column("MessageCount")]
        public int MessageCount { get; set; }

        [Column("MediaCount")]
        public int MediaCount { get; set; }

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; }
    }
}
