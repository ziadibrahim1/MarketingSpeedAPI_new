namespace MarketingSpeedAPI.Models.MarketingSpeedAPI.Services
{
    using global::MarketingSpeedAPI.Models.MarketingSpeedAPI.Models;
    using System.IO.Pipelines;
    using System.Net.Http.Json;
    using System.Text.Json;

    public class TelegramClientManager
    {
        private readonly HttpClient _http;
        private readonly TelegramSessionService _sessionService;

        public TelegramClientManager(
            HttpClient http,
            TelegramSessionService sessionService)
        {
            _http = http;
            _sessionService = sessionService;
        }

        public async Task<string> StartLoginAsync(long userId, string phone)
        {
            var res = await _http.PostAsJsonAsync("/start-login", new
            {
                userId,
                phone
            });

            var data = await res.Content.ReadFromJsonAsync<TgStatusResponse>();
            return data!.Status;
        }

        public async Task<string> ConfirmCodeAsync(long userId, string code)
        {
            var res = await _http.PostAsJsonAsync("/confirm-code", new
            {
                userId,
                code
            });

            var data = await res.Content.ReadFromJsonAsync<TgStatusResponse>();

            if (data?.Status == "connected")
            {
                await _sessionService.SaveSessionAsync(
                    userId: userId,
                    phone: data.Phone!,
                    telegramUserId: data.TelegramUserId!,
                    username: data.Username,
                    firstName: data.FirstName,
                    is2Fa: data.Is2Fa
                );
            }

            return data!.Status;
        }

        public async Task<string> ConfirmPasswordAsync(long userId, string password)
        {
            var res = await _http.PostAsJsonAsync("/confirm-password", new
            {
                userId,
                password
            });

            var data = await res.Content.ReadFromJsonAsync<TgStatusResponse>();

            if (data?.Status == "connected")
            {
                await _sessionService.SaveSessionAsync(
                    userId: userId,
                    phone: data.Phone!,
                    telegramUserId: data.TelegramUserId!,
                    username: data.Username,
                    firstName: data.FirstName,
                    is2Fa: true
                );
            }

            return data!.Status;
        }

        public async Task<bool> HasActiveSessionAsync(long userId)
        {
            var res = await _http.GetAsync($"/session-status/{userId}");

            if (!res.IsSuccessStatusCode)
                return false;

            var status = await res.Content.ReadFromJsonAsync<TgSessionStatus>();

            if (status == null || status.IsLoggedOut)
            {
                await _sessionService.MarkSessionInactiveAsync(userId);
                return false;
            }

            return true;
        }



        public async Task<List<TeleGroupInfo>> GetDialogsAsync(long userId)
        {
            var res = await _http.GetAsync($"/dialogs/{userId}");

            if (!res.IsSuccessStatusCode)
                throw new Exception($"Failed to fetch dialogs: {res.StatusCode}");

            // اقرأ JSON مباشرة كـ List<TeleGroupInfo>
            var result = await res.Content.ReadFromJsonAsync<List<TeleGroupInfo>>();

            // رجع قائمة فارغة لو ما كانش في بيانات
            return result ?? new List<TeleGroupInfo>();
        }

        public async Task<GroupsAndChannelsResponse?> GetGroupsAndChannelsAsync(long userId)
        {

            var response = await _http.GetAsync($"/groups-and-channels/{userId}");

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();

            return System.Text.Json.JsonSerializer.Deserialize<GroupsAndChannelsResponse>(
                json,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        public async Task<bool> ClearDialogAsync(
     long userId,
     long id,
     long? accessHash,
     string? peerType)
        {
            var response = await _http.PostAsJsonAsync(
                $"dialogs/clear/{userId}",
                new
                {
                    id = id,
                    access_hash = accessHash,
                    peer_type = peerType
                });

            if (!response.IsSuccessStatusCode)
                return false;

            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (result != null && result.ContainsKey("success"))
                return Convert.ToBoolean(result["success"]);

            return false;
        }

        public async Task SendMessageAsync(long userId, long peerId, string message)
        {
            await _http.PostAsJsonAsync("/send", new
            {
                userId,
                peerId,
                message
            });
        }
        public async Task<TgChannelsResponse> GetChannelsAsync(long userId)
        {
            var res = await _http.GetAsync($"/channels/{userId}");
            res.EnsureSuccessStatusCode();

            var data = await res.Content.ReadFromJsonAsync<TgChannelsResponse>();

            return data ?? new TgChannelsResponse();
        }

        public async Task<TgChannelsResponse> GetSendableChannelsAsync(long userId)
        {
            var res = await _http.GetAsync($"channels/can-send/{userId}");
            res.EnsureSuccessStatusCode();

            var rawJson = await res.Content.ReadAsStringAsync();

            var data = JsonSerializer.Deserialize<TgChannelsResponse>(
                rawJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (data != null && data.Items != null)
            {
                data.Items = data.Items
                    .Where(c => c.CanSend)
                    .ToList();
            }

            return data ?? new TgChannelsResponse();
        }
 
        public async Task<TgCreateResponse> GetCreateChannelsAsync(long userId)
        {
            var res = await _http.GetAsync($"channels/Create/Admin/{userId}");
            res.EnsureSuccessStatusCode();

            var rawJson = await res.Content.ReadAsStringAsync();

            var data = JsonSerializer.Deserialize<TgCreateResponse>(
                rawJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (data != null && data.Items != null)
            {
                data.Items = data.Items
                    .Where(c => c.CanSend)
                    .ToList();
            }

            return data ?? new TgCreateResponse();
        }
 
        public async Task<TgChannelsResponse> GetOpeednChannelsAsync(long userId)
        {
            var res = await _http.GetAsync($"channels/can-view-members/{userId}");
            res.EnsureSuccessStatusCode();

            var rawJson = await res.Content.ReadAsStringAsync();

       
            var data = JsonSerializer.Deserialize<TgChannelsResponse>(
                rawJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (data != null && data.Items != null)
            {
                data.Items = data.Items
                    .Where(c => c.CanViewMembers)
                    .ToList();
            }

            return data ?? new TgChannelsResponse();
        }


        public async Task<SendResult> SendToMembers(long userId, long chatId, string message)
        {
            var response = await _http.PostAsJsonAsync(
                $"members/send/{userId}",
                new
                {
                    chat_id = chatId,
                    message = message
                });

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SendResult>(
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            return result ?? new SendResult();
        }

        public async Task<SendResult> SendToGroups(long userId, List<long> chatIds, string message)
        {
            var response = await _http.PostAsJsonAsync(
                $"groups/send/{userId}",
                new
                {
                    chat_ids = chatIds,
                    message = message
                });

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SendResult>(
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            return result ?? new SendResult();
        }


        // ===== جلب الأعضاء لكل قناة/جروب =====
        public async Task<TgMembersResponse> GetGroupMembersAsync(long userId, string channelId)
        {
            var res = await _http.GetAsync($"/group-members/{userId}/{channelId}");
            res.EnsureSuccessStatusCode();

            var data = await res.Content.ReadFromJsonAsync<TgMembersResponse>();
            return data ?? new TgMembersResponse();
        }



        // ===== جلب القنوات مع الأعضاء مباشرة =====
        public async Task<TgChannelsResponse> GetChannelsWithMembersAsync(long userId)
        {
            var channels = await GetChannelsAsync(userId);

            foreach (var ch in channels.Channels)
            {
                var members = await GetGroupMembersAsync(userId, ch.Id.ToString());
            }

            return channels;
        }
        public async Task<List<TelegramChatInfo>> GetPrivateChatsAsync(long userId)
        {
            var res = await _http.GetAsync($"/chats/{userId}");

            if (!res.IsSuccessStatusCode)
                throw new Exception($"Failed to fetch Telegram chats: {res.StatusCode}");

            var data = await res.Content.ReadFromJsonAsync<List<TelegramChatInfo>>(
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            return data ?? new List<TelegramChatInfo>();
        }
        public async Task<SendResult> SendToPhonesAsync(
    long userId,
    List<string> phones,
    string message)
        {
            var response = await _http.PostAsJsonAsync(
                $"send-to-phones/{userId}",
                new
                {
                    phones,
                    message
                });

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SendResult>(
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            return result ?? new SendResult();
        }
        public async Task<TgCreateGroupResponse> CreateGroupAsync( long userId, string title, List<long> userIds)
        {
            var response = await _http.PostAsJsonAsync(
                $"groups/create/{userId}",
                new
                {
                    title,
                    user_ids = userIds
                });

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TgCreateGroupResponse>(
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            return result ?? new TgCreateGroupResponse();
        }
        public async Task AddMembersToGroupAsync(
            long userId,
            long chatId,
            List<long> userIds)
        {
            var response = await _http.PostAsJsonAsync(
                $"groups/add-members/{userId}",
                new
                {
                    chat_id = chatId,
                    user_ids = userIds
                });

            response.EnsureSuccessStatusCode();
        }

        public async Task LogoutAsync(long userId)
        {
            var s =await _http.PostAsync($"/logout/{userId}", null);
            if (s.StatusCode == System.Net.HttpStatusCode.OK)
            {
                await _sessionService.DeactivateSessionsAsync(userId);
            }
           
        }
    }
}
