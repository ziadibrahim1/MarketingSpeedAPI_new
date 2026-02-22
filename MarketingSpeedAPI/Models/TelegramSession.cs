using System.Text.Json.Serialization;

namespace MarketingSpeedAPI.Models
{
    public class TelegramSession
    {
        public long Id { get; set; }

        public long UserId { get; set; }
        public string PhoneNumber { get; set; } = null!;

        public long TelegramUserId { get; set; }
        public string? Username { get; set; }
        public string? FirstName { get; set; }

        public bool Is2Fa { get; set; }
        public bool IsActive { get; set; }

        public string SessionFile { get; set; } = null!;
        public DateTime LastLoginAt { get; set; }
    }
    public class TgChannelDto
    {
        public long Id { get; set; }
        public string? Title { get; set; }
        public string? Username { get; set; }
        public int? MembersCount { get; set; }
        public bool IsChannel { get; set; }
        public bool IsGroup { get; set; }
        public bool CanViewMembers { get; set; }
        public List<TeleGroupMember> Members { get; internal set; }
    }
    public class TgMemberDto
    {
        public long Id { get; set; }
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public bool IsBot { get; set; }
    }
    public class SendTeleMembersRequest
    {
        public long UserId { get; set; }
        public long ChatId { get; set; }
        public string Message { get; set; } = string.Empty;
    }
    public class SendToGroupsRequest
    {
        public long UserId { get; set; }
        public List<long> ChatIds { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }


    public class TgMembersResponse
    {
        public int Count { get; set; }
        public List<TeleGroupMember> Members { get; set; } = new();
    }

    public class TeleGroupInfo
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public int ParticipantCount { get; set; }
        public bool IsChannel { get; set; }
        public bool IsGroup { get; set; }
    }
    public class SendResult
    {
        public int Total { get; set; }
        public int Sent { get; set; }
        public int Failed { get; set; }

        [JsonPropertyName("flood_wait")]
        public int FloodWait { get; set; }

        public List<SendFailure>? Failures { get; set; }
    }
    public class SendToPhonesRequest
    {
        public long UserId { get; set; }
        public List<string> Phones { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }

    public class TelegramChatInfo
    {
        public long Id { get; set; }
        public string? Type { get; set; }
        public string? Name { get; set; }
        public string? Username { get; set; }
        public int Unread_Count { get; set; }
    }

    public class SendFailure
    {
        [JsonPropertyName("user_id")]
        public long UserId { get; set; }

        public string? Reason { get; set; }

        public string? Details { get; set; }

        public int? Seconds { get; set; }
    }

    public class TgChannelsResponse
    {
        public int Count { get; set; }
        public List<TgChannelDto> Channels { get; set; } = new();
        public List<TgChannelItem> Items { get; set; } = new();
    }
    public class TgCreateResponse
    {
        public int Count { get; set; }
        public List<TgChannelDto> Channels { get; set; } = new();
        public List<TgCreateItem> Items { get; set; } = new();
    }
    public class TgCreateItem
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public bool CanSend { get; set; }
        public bool IsAdmin { get; set; }  // مهمة جداً للتقسيم
    }

    public class TgChannelItem
    {
        public long Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public bool CanViewMembers { get; set; }
        public bool? IsAdmin { get; set; }

        public bool CanSend { get; set; }
    }

    public class TgStatusResponse
    {
        public string Status { get; set; } = "";
        public string? Phone { get; set; }
        public long? TelegramUserId { get; set; }
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public bool Is2Fa { get; set; }
    }

    public class TeleGroupMember
    {
        public long Id { get; set; }          // ✅ رقم
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public bool IsBot { get; set; }
    }
    public class GroupsAndChannelsResponse
    {
        public int Count { get; set; }
        public List<GroupChannelItem> Items { get; set; } = new();
    }

    public class GroupChannelItem
    {
        public long Id { get; set; }

        public long? Access_Hash { get; set; }   // مهم للقنوات

        public string Peer_Type { get; set; } = string.Empty;   // chat أو channel

        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public bool CanSend { get; set; }

        public bool IsAdmin { get; set; }

        public int? MembersCount { get; set; }

        public string? Username { get; set; }
    }
    public class ClearDialogRequest
    {
        public long UserId { get; set; }
        public long Id { get; set; }
        public long? AccessHash { get; set; }
        public string PeerType { get; set; }  
    }

    public class JoinByLinkRequest
    {
        public long UserId { get; set; }
        public string Link { get; set; } = string.Empty;
    }
    public class TgJoinResponse
    {
        public bool Success { get; set; }
        public string? Type { get; set; }
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public double? FloodWait { get; set; }
    }
    public class TeleGroupInfos
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int ParticipantCount { get; set; }
        public bool IsChannel { get; set; }
        public bool IsGroup { get; set; }
        public List<TeleGroupMember> Members { get; set; } = new List<TeleGroupMember>();
    }
 
    public class TgSessionStatus
    {
        public bool IsConnected { get; set; }
        public bool HasSession { get; set; }
        public bool IsLoggedOut { get; set; }
    }

}
