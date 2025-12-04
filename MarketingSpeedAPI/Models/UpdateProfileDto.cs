namespace MarketingSpeedAPI.Models
{
    public static class WhatsAppSafetySettings
{
    public const int MinDelaySeconds = 7;
    public const int MaxDelaySeconds = 10;

    public const int GroupsPerBatch = 10;
    public const int BatchRestSeconds = 70; 

    public const int MaxConsecutiveFailures = 3;  
}
    public class DeleteUserDto
    {
        public string Email { get; set; } = string.Empty;
    }
    public class SendSingleGroupRequest
    {
        public string GroupId { get; set; }
        public string? Message { get; set; }
        public List<string>? ImageUrls { get; set; }
        public long? MainMessageId { get; set; }
        public bool? fromChates { get; set; }
}
    public class SendSingleMemberRequest
    {
        public int PlatformId { get; set; }           // معرف المنصة (مثل WhatsApp = 1)
        public string? Recipient { get; set; }       // رقم أو معرف المستلم
        public string? Message { get; set; }         // نص الرسالة
        public List<string>? ImageUrls { get; set; } // الصور أو المرفقات
        public long? MainMessageId { get; set; }     // ID للرسالة الأصلية في الـ DB (اختياري)
    }

    public class UpdateProfileDto
    {
        public string? First_Name { get; set; }
        public string? Middle_Name { get; set; }
        public string? Last_Name { get; set; }
        public string? Country_Code { get; set; }
        public string? Phone { get; set; }
        public string? Country { get; set; }
        public string? Password { get; set; }
        public string? Email { get; set; }
        public int? CountryId { get; set; }
        public string? CityName { get; set; }
        public int? City { get; set; }
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
