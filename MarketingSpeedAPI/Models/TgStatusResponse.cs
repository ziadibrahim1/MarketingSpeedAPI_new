namespace MarketingSpeedAPI.Models
{
    namespace MarketingSpeedAPI.Models
    {
        public class TgStatusResponse
        {
            public string Status { get; set; } = "";
            public long TelegramUserId { get; set; }    
            public string? Username { get; set; }
            public string? FirstName { get; set; }
            public bool Is2Fa { get; set; }
            public string? Phone { get; set; }

        }

        public class HasSessionResponse
        {
            public bool IsSuccessStatusCode { get; set; }
        }

        public class TgSessionStatus
        {
            public bool IsConnected { get; set; }
            public bool IsAuthorized { get; set; }
            public bool IsLoggedOut { get; set; }
        }
        public class TgCreateGroupResponse
        {
            public long Group_Id { get; set; }
            public string? Title { get; set; }
        }
        public class CreateTelegramGroupRequest
        {
            public long UserId { get; set; }
            public string Title { get; set; } = null!;
            public List<long> UserIds { get; set; } = new();
        }

        public class AddTelegramGroupMembersRequest
        {
            public long UserId { get; set; }
            public long GroupId { get; set; }
            public List<long> UserIds { get; set; } = new();
        }
    }

}
namespace MarketingSpeedAPI.Models
{
    public class SendTelegramMessage
    {
        public long UserId { get; set; }
        public long PeerId { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
