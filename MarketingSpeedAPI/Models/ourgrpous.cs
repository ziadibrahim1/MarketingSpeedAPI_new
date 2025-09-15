namespace MarketingSpeedAPI.Models
{
    public class OurGroupsCountry
    {
        public int Id { get; set; }
        public string NameAr { get; set; }
        public string NameEn { get; set; }
        public string IsoCode { get; set; }
        public string PhoneCode { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        public ICollection<CompanyGroup> CompanyGroups { get; set; }
    }

    public class OurGroupsCategory
    {
        public int Id { get; set; }
        public string NameAr { get; set; }
        public string NameEn { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        public ICollection<CompanyGroup> CompanyGroups { get; set; }
    }

    public class CompanyGroup
    {
        public int Id { get; set; }
        public int PlatformId { get; set; }
        public string GroupName { get; set; }
        public string Description { get; set; }
        public string InviteLink { get; set; }
        public int? CountryId { get; set; }
        public int? CategoryId { get; set; }
        public decimal Price { get; set; }
        public bool IsActive { get; set; }
        public bool IsHidden { get; set; }
        public string SendingStatus { get; set; }
        public DateTime CreatedAt { get; set; }

        public OurGroupsCountry OurGroupsCountry { get; set; }
        public OurGroupsCategory OurGroupsCategory { get; set; }
        public string CountryNameAr { get; internal set; }
        public string CategoryNameAr { get; internal set; }
        public string CountryNameEn { get; internal set; }
        public string CategoryNameEn { get; internal set; }
        public int Members { get; internal set; }
    }


}
