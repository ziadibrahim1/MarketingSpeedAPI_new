public class LoginDto
{
    public required string Email { get; set; }
    public required string Password { get; set; }
    public string? Language { get; set; } = "en"; // لتخصيص رسائل الخطأ
}
