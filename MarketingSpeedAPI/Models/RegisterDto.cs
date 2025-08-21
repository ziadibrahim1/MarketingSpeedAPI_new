namespace MarketingSpeedAPI.DTOs
{
    public class RegisterDto
    {
        // معلومات الاسم
        public required string first_name { get; set; }
        public string? middle_name { get; set; }
        public  string? last_name { get; set; }

        // بيانات التواصل
        public  string? country_code { get; set; }
        public  string? phone { get; set; }
        public required string email { get; set; }
        public  string? country { get; set; }
        public int? city { get; set; }

        // نوع المستخدم والمؤسسة
        public  string? user_type { get; set; } // "company" أو "individual"
        public string? company_name { get; set; }
        public string? description { get; set; } // وصف المؤسسة أو نبذة شخصية

        // إعدادات التطبيق
        public string? language { get; set; } = "ar";
        public string? theme { get; set; } = "light";
        public bool accept_notifications { get; set; } = true;
        public bool accept_terms { get; set; } = false;

        // كلمة المرور
        public required string password_hash { get; set; }
    }


}
