public class LoginDto
{
    public required string email { get; set; }
    public required string password_hash { get; set; }
    public string? language { get; set; } = "en"; // لتخصيص رسائل الخطأ
}
 