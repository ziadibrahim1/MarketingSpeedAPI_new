public class User
{
    public int Id { get; set; }
    public required string Email { get; set; }

    public string FullName { get; set; } = null!;
    public bool IsEmailVerified { get; set; } = false;
    public required string Password_Hash { get; set; }
    public string? VerificationCode { get; set; }
    public DateTime VerificationCodeExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    
    

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
