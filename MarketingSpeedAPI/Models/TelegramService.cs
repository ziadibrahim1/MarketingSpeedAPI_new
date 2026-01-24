using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TdLib;
using TdApi = TdLib.TdApi;
using MarketingSpeedAPI.Models;

namespace MarketingSpeedAPI.Controllers
{
    public class TelegramClientManager
    {
        private readonly ConcurrentDictionary<long, TdClient> _clients = new();
        private readonly ConcurrentDictionary<long, TdApi.AuthorizationState> _authStates = new();

        private readonly int _apiId;
        private readonly string _apiHash;
        private readonly string _baseDataDir;

        public TelegramClientManager(int apiId, string apiHash, string baseDataDir = "tdlib_data")
        {
            _apiId = apiId;
            _apiHash = apiHash;
            _baseDataDir = baseDataDir;
        }

        private string GetUserDataDir(long userId)
            => Path.Combine(AppContext.BaseDirectory, _baseDataDir, userId.ToString());

        private void EnsureUserDataDirExists(long userId)
        {
            var dir = GetUserDataDir(userId);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private TdClient CreateClient(long userId)
        {
            var client = new TdClient();

            client.UpdateReceived += (sender, update) =>
            {
                if (update is TdApi.Update.UpdateAuthorizationState u)
                {
                    _authStates[userId] = u.AuthorizationState;
                    Console.WriteLine($"[TDLib] User {userId} state = {u.AuthorizationState.GetType().Name}");
                }
            };

            client.Execute(new TdApi.SetLogVerbosityLevel { NewVerbosityLevel = 1 });
            return client;
        }

        public TdClient GetOrCreateClient(long userId)
            => _clients.GetOrAdd(userId, CreateClient);

        public async Task EnsureInitedAsync(long userId, TdClient client)
        {
            var parameters = new TdApi.SetTdlibParameters
            {
                ApiId = _apiId,
                ApiHash = _apiHash,
                SystemLanguageCode = "en",
                DeviceModel = "Server",
                ApplicationVersion = "1.0",
                DatabaseDirectory = GetUserDataDir(userId),
                FilesDirectory = GetUserDataDir(userId),
                UseFileDatabase = true,
                UseChatInfoDatabase = true,
                UseMessageDatabase = true,
                UseTestDc = false
            };

            await client.ExecuteAsync(parameters);
        }

        private async Task<TdApi.AuthorizationState> WaitForAnyStateAsync(long userId, int timeoutMs = 5000)
        {
            var start = DateTime.Now;
            TdApi.AuthorizationState state = null;

            while (!_authStates.TryGetValue(userId, out state))
            {
                if ((DateTime.Now - start).TotalMilliseconds > timeoutMs)
                {
                    // لو لم يصل أي تحديث خلال الوقت المحدد، اعتبر الجلسة مغلقة
                    return new TdApi.AuthorizationState.AuthorizationStateClosed();
                }
                await Task.Delay(100);
            }

            return state;
        }

        // 1️⃣ بدء تسجيل الدخول
        public async Task<string> StartLoginAsync(long userId, string phoneNumber)
        {
            EnsureUserDataDirExists(userId);
            var client = GetOrCreateClient(userId);

            await EnsureInitedAsync(userId, client);

            var state = await WaitForAnyStateAsync(userId);

            // إذا الجلسة جاهزة بالفعل
            if (state is TdApi.AuthorizationState.AuthorizationStateReady)
                return "connected";

            // أرسل رقم الهاتف إذا الجلسة ليست جاهزة
            await client.ExecuteAsync(new TdApi.SetAuthenticationPhoneNumber
            {
                PhoneNumber = phoneNumber
            });

            return "code_sent";
        }

        // 2️⃣ تأكيد الكود
        public async Task<string> ConfirmCodeAsync(long userId, string code)
        {
            var client = GetOrCreateClient(userId);

            await client.ExecuteAsync(new TdApi.CheckAuthenticationCode { Code = code });

            if (_authStates.TryGetValue(userId, out var state) &&
                state is TdApi.AuthorizationState.AuthorizationStateWaitPassword)
                return "need_2fa_password";

            // انتظر حتى تصبح الجلسة جاهزة
            await WaitForAnyStateAsync(userId, 5000);

            return "connected";
        }

        // 3️⃣ تأكيد كلمة مرور 2FA
        public async Task<string> ConfirmPasswordAsync(long userId, string password)
        {
            var client = GetOrCreateClient(userId);

            await client.ExecuteAsync(new TdApi.CheckAuthenticationPassword { Password = password });

            await WaitForAnyStateAsync(userId, 5000);

            return "connected";
        }

        // 4️⃣ معرفة الحالة
        public async Task<string> GetStatusAsync(long userId)
        {
            var state = await WaitForAnyStateAsync(userId);

            return state switch
            {
                TdApi.AuthorizationState.AuthorizationStateReady => "connected",
                TdApi.AuthorizationState.AuthorizationStateWaitPhoneNumber => "wait_phone",
                TdApi.AuthorizationState.AuthorizationStateWaitCode => "wait_code",
                TdApi.AuthorizationState.AuthorizationStateWaitPassword => "wait_password",
                TdApi.AuthorizationState.AuthorizationStateLoggingOut => "logging_out",
                TdApi.AuthorizationState.AuthorizationStateClosing => "closing",
                TdApi.AuthorizationState.AuthorizationStateClosed => "closed",
                _ => "unknown"
            };
        }

        // 5️⃣ التحقق من الاتصال الحقيقي
        public async Task<bool> IsConnectedAsync(long userId)
        {
            var status = await GetStatusAsync(userId);
            return status == "connected";
        }

        // 6️⃣ تسجيل الخروج
        public async Task LogoutAsync(long userId)
        {
            if (_clients.TryGetValue(userId, out var client))
            {
                try { await client.ExecuteAsync(new TdApi.LogOut()); } catch { }
                try { await client.ExecuteAsync(new TdApi.Close()); } catch { }

                _clients.TryRemove(userId, out _);
                _authStates[userId] = new TdApi.AuthorizationState.AuthorizationStateClosed();
            }
        }

        // 7️⃣ إعادة تهيئة جميع العملاء عند بدء التشغيل
        public async Task InitializeAllClientsAsync(Func<IQueryable<UserAccount>> getConnectedUsers)
        {
            var connectedUsers = getConnectedUsers().ToList();

            foreach (var user in connectedUsers)
            {
                EnsureUserDataDirExists(user.UserId);
                var client = GetOrCreateClient(user.UserId);
                await EnsureInitedAsync(user.UserId, client);
                // TDLib سيعيد تحميل الجلسة من الملفات تلقائياً إذا كانت موجودة
            }
        }
    }
}
