using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketingSpeedAPI.Models
{
    public class Referral
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ReferrerId { get; set; }   // المستخدم الداعي

        [Required]
        [MaxLength(50)]
        public string ReferralCode { get; set; }

        public int? ReferredUserId { get; set; } // المستخدم الجديد اللي استخدم الكود

        public DateTime? UsedAt { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "pending"; // pending | completed

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // علاقات
        [ForeignKey("ReferrerId")]
        public User Referrer { get; set; }

        [ForeignKey("ReferredUserId")]
        public User ReferredUser { get; set; }
    }
}
