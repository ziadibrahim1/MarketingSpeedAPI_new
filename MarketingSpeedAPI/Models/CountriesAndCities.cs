namespace MarketingSpeedAPI.Models
{
    public class City
    {
        public int Id { get; set; }
        public int CountryId { get; set; }
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    public class Country
    {
        public int Id { get; set; }
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string IsoCode { get; set; } = string.Empty;
        public string PhoneCode { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        public List<City> Cities { get; set; } = new List<City>();
    }
    public class InviteRequest
    {
        public string? Code { get; set; }
    }

}