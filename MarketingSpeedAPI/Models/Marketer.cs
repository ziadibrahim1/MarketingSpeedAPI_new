namespace MarketingSpeedAPI.Models
{
    public class Marketer
    {
        public int Id { get; set; }
        public string PromoCode { get; set; }
        public int PointsAccumulated { get; set; }
        public bool IsFrozen { get; set; }
        public bool IsDeleted { get; set; }
    }

}
