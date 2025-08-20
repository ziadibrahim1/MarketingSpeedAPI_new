namespace MarketingSpeedAPI.Models
{
    public class CountryDto
    {
        public int Id { get; set; }
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string PhoneCode { get; set; } = string.Empty;

        public List<CityDto> Cities { get; set; } = new List<CityDto>();
    }
}
