using System.ComponentModel.DataAnnotations;

namespace MarketingSpeedAPI.Models
{
    public class Marketer
    {
        public int Id { get; set; }
        public int SupervisorId { get; set; }
        public string PromoCode { get; set; }
        public int PointsAccumulated { get; set; }
        public decimal? totalDueAmount { get; set; }
        public decimal? PointPrice { get; set; }

        public bool IsFrozen { get; set; }
        public bool IsDeleted { get; set; }
    }
    public class Supervisor
    {
        [Key]
        public int Id { get; set; }

        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";

        public string? Country { get; set; }
        public string? City { get; set; }
        public string? Bank { get; set; }
        public string? AccountNumber { get; set; }
        public string? Phone { get; set; }

        public string Email { get; set; } = "";
        public string Password { get; set; } = "";

        public decimal? AmountDue { get; set; }
        public decimal PointPrice { get; set; }
        public double? Age { get; set; }

        public bool IsFrozen { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? CreatedAt { get; set; }
        public bool isWithdrawalPending { get; set; }

        public int? DashboardUserId { get; set; }

        public ICollection<Marketer> Marketers { get; set; } = new List<Marketer>();
    }
}
