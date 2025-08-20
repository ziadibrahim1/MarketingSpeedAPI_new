namespace MarketingSpeedAPI.Models
{
    public class UpdateProfileDto
    {
        public string? First_Name { get; set; }
        public string? Middle_Name { get; set; }
        public string? Last_Name { get; set; }
        public string? Country_Code { get; set; }
        public string? Phone { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? User_Type { get; set; }
        public string? Company_Name { get; set; }
        public string? Description { get; set; }
        public string? Profile_Picture { get; set; }
        public bool? Accept_Notifications { get; set; }
        public bool? Accept_Terms { get; set; }
        public string? Language { get; set; }
        public string? Theme { get; set; }
        public DateTime? last_seen { get; set; }
    }

}
