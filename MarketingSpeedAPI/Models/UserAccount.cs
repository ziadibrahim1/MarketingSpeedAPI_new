namespace MarketingSpeedAPI.Models
{
    public class UserAccount
    {
        public int Id { get; set; }
        public long UserId { get; set; }
        public int PlatformId { get; set; }
        public string AccountIdentifier { get; set; }
        public string DisplayName { get; set; }
        public DateTime? QrCodeExpiry { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public bool IsVerified { get; set; }
        public string Status { get; set; }
        public DateTime? ConnectedAt { get; set; }
        public DateTime? LastActivity { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public int? WasenderSessionId { get; set; }
    }
    public class WasenderSettings
    {
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
    }
    public class ConnectQrRequest
    {
        public int UserId { get; set; }
        public int PlatformId { get; set; }
    }

    public class CreateSessionRequest
    {
        public int UserId { get; set; }
        public int PlatformId { get; set; }
        public string PhoneNumber { get; set; }
        public string DisplayName { get; set; }
    }

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }
    }
}
