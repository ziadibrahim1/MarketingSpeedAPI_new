using System.ComponentModel.DataAnnotations.Schema;

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
    public class UserJoinedGroup
    {
        public int Id { get; set; }
        public int user_id { get; set; }
        public string group_invite_code { get; set; } = string.Empty;
        public string group_name { get; set; } = string.Empty;
        public DateTime joined_at { get; set; }
        public bool is_active { get; set; } = true; 
    }
    public class GroupListResponse
    {
        public int Limit { get; set; }
        public List<CompanyGroup> Groups { get; set; }
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
        [NotMapped]
        public int Add_groups_limit { get; set; }
        [NotMapped]
        public string? CategoryNameAr { get; set; }

        [NotMapped]
        public string? CategoryNameEn { get; set; }

        public OurGroupsCountry OurGroupsCountry { get; set; }
        public OurGroupsCategory OurGroupsCategory { get; set; }
        [NotMapped]
        public string CountryNameAr { get; internal set; }
        [NotMapped]
        public string CountryNameEn { get; internal set; }
        [NotMapped]
        public int Members { get; internal set; }
    }

    public class LeftGroup
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string GroupId { get; set; }
        public string GroupName { get; set; }
        public string InviteLink { get; set; }
        public DateTime LeftAt { get; set; }
    }


    public class LeaveGroupRequest
    {
        public string Jid { get; set; }           // معرف المجموعة (jid)
        public string? GroupName { get; set; }
        // اختياري، اسم المجموعة
    }


}
