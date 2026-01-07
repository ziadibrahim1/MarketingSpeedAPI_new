namespace MarketingSpeedAPI.Models
{
    public class SubscriptionFeatureGrant
    {
        public ulong Id { get; set; }

        public ulong UserId { get; set; }
        public uint SubscriptionId { get; set; }
        public ulong PackageId { get; set; }
        public ulong FeatureId { get; set; }

        public int GrantedCount { get; set; }
        public int UsedCount { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

}
