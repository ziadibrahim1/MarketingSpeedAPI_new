namespace MarketingSpeedAPI.DTOs
{
    public class RegisterDto
    {
        // معلومات الاسم
        public required string FirstName { get; set; }
        public string? MiddleName { get; set; }
        public required string LastName { get; set; }

        // بيانات التواصل
        public required string CountryCode { get; set; }
        public required string Phone { get; set; }
        public required string Email { get; set; }
        public required string Country { get; set; }
        public required string City { get; set; }

        // نوع المستخدم والمؤسسة
        public required string UserType { get; set; } // "company" أو "individual"
        public string? CompanyName { get; set; }
        public string? Description { get; set; } // وصف المؤسسة أو نبذة شخصية

        // إعدادات التطبيق
        public string? Language { get; set; } = "ar";
        public string? Theme { get; set; } = "light";
        public bool AcceptNotifications { get; set; } = true;
        public bool AcceptTerms { get; set; } = false;

        // كلمة المرور
        public required string Password { get; set; }
    }


}
