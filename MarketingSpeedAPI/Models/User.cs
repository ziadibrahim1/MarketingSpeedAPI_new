public class User
{
    public int id { get; set; }

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
    public int? subscreption { get; set; }

    // نوع المستخدم والمؤسسة
    public  string? user_type { get; set; } // "company" أو "individual"
    public string? company_name { get; set; }
    public string? description { get; set; } // وصف المؤسسة أو نبذة شخصية

    // إعدادات التطبيق
    public string language { get; set; } = "ar"; // "ar" أو "en"
    public string theme { get; set; } = "light"; // "light" أو "dark"
    public string status { get; set; } = "active"; // "active", "inactive", "banned"
    public string? profile_picture { get; set; } // رابط الصورة في Firebase Storage
    public bool accept_notifications { get; set; } = true;
    public bool accept_terms { get; set; } = false;

    // كلمات المرور والتحقق
    public required string password_hash { get; set; }
    public bool is_email_verified { get; set; } = false;
    public string? verification_code { get; set; }
    public required DateTime verification_code_expires_at { get; set; } = DateTime.UtcNow.AddMinutes(2);

    // تتبع النشاط
    public DateTime? last_seen { get; set; }
    public DateTime created_at { get; set; } = DateTime.UtcNow;
    public DateTime updated_at { get; set; } = DateTime.UtcNow;
}


