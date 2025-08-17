public class User
{
    public int Id { get; set; }

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
    public string Language { get; set; } = "ar"; // "ar" أو "en"
    public string Theme { get; set; } = "light"; // "light" أو "dark"
    public string Status { get; set; } = "active"; // "active", "inactive", "banned"
    public string? ProfilePicture { get; set; } // رابط الصورة في Firebase Storage
    public bool AcceptNotifications { get; set; } = true;
    public bool AcceptTerms { get; set; } = false;

    // كلمات المرور والتحقق
    public required string PasswordHash { get; set; }
    public bool IsEmailVerified { get; set; } = false;
    public string? VerificationCode { get; set; }
    public DateTime? VerificationCodeExpiresAt { get; set; }

    // تتبع النشاط
    public DateTime? LastSeen { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CountriesAndCities
{
    public int Id { get; set; }
    public string CountryNameEn { get; set; }
    public string CountryNameAr { get; set; }
    public string CityNameEn { get; set; }
    public string CityNameAr { get; set; }
}

public class TermsAndConditions
{
    public int Id { get; set; }
    public string Language { get; set; }
    public string Content { get; set; }
}
