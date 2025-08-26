using System.ComponentModel.DataAnnotations;

namespace MarketingSpeedAPI.Models
{
    public class SocialAccount
    {
        [Key]
        public long id { get; set; }

        [Required]
        public string platform { get; set; }

        [Required]
        public string url { get; set; }

        public bool is_official { get; set; } = true;

        public DateTime created_at { get; set; } = DateTime.UtcNow;
        public DateTime updated_at { get; set; } = DateTime.UtcNow;
    }

    public class DirectContact
    {
        [Key]
        public long id { get; set; }

        [Required]
        public string type { get; set; } // whatsapp, phone, email, website

        [Required]
        public string value { get; set; }

        public DateTime created_at { get; set; } = DateTime.UtcNow;
        public DateTime updated_at { get; set; } = DateTime.UtcNow;
    }
}
