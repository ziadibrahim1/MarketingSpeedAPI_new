using MarketingSpeedAPI.Controllers;
using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using MarketingSpeedAPI.Models.MarketingSpeedAPI.Models;
using MarketingSpeedAPI.Models.MarketingSpeedAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestSharp;
using System.Text.Json;
using Windows.UI;
using static System.Net.WebRequestMethods;

namespace MarketingSpeedAPI.Controllers
{

    [ApiController]
    [Route("api/telegram")]
    public class TelegramController : ControllerBase
    {
        private readonly TelegramClientManager _tg;
        private readonly AppDbContext _context;
        public TelegramController(AppDbContext context, TelegramClientManager tg)
        {
            _tg = tg;
            _context = context;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(StartLoginRequest req)
            => Ok(new { status = await _tg.StartLoginAsync(req.UserId, req.PhoneNumber) });

        [HttpPost("confirm-code")]
        public async Task<IActionResult> Code(ConfirmCodeRequest req)
            => Ok(new { status = await _tg.ConfirmCodeAsync(req.UserId, req.Code) });

        [HttpPost("confirm-password")]
        public async Task<IActionResult> Password(ConfirmPasswordRequest req)
            => Ok(new { status = await _tg.ConfirmPasswordAsync(req.UserId, req.Password) });

        [HttpGet("has-active-session/{userId}")]
        public async Task<IActionResult> HasActiveSession(long userId)
        {
            bool hasSession = await _tg.HasActiveSessionAsync(userId);
            return Ok(new { hasSession });
        }

        [HttpGet("get-chats/{userId}")]
        public async Task<IActionResult> GetTelegramChats(long userId)
        {
            //var account = await _context.user_accounts
            //    .FirstOrDefaultAsync(a =>
            //        a.UserId == (int)userId &&
            //        a.PlatformId == 2 &&   // 2 = Telegram
            //        a.Status == "connected");

            //if (account == null)
            //    return Ok(new { success = false, message = "No connected Telegram account found" });

            try
            {
                var chats = await _tg.GetPrivateChatsAsync(userId);

                return Ok(new
                {
                    success = true,
                    message = "Telegram chats fetched successfully",
                    data = chats
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    message = "Error fetching Telegram chats",
                    error = ex.Message
                });
            }
        }

        [HttpPost("send-to-phones")]
        public async Task<IActionResult> SendToPhones([FromBody] SendToPhonesRequest request)
        {
            try
            {
                var result = await _tg.SendToPhonesAsync(
                    request.UserId,
                    request.Phones,
                    request.Message
                );

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpGet("dialogs/{userId}")]
        public async Task<IActionResult> Dialogs(long userId)
            => Ok(await _tg.GetSendableChannelsAsync(userId));

        [HttpGet("create/dialogs/{userId}")]
        public async Task<IActionResult> CreateDialogs(long userId)
        {
            var data = await _tg.GetCreateChannelsAsync(userId);
            if (data == null || data.Items == null)
                return Ok(new
                {
                    adminGroups = new List<object>(),
                    memberGroups = new List<object>()
                });

            var adminGroups = data.Items
                .Where(c => c.IsAdmin)   // لازم تكون موجودة في TgChannelItem
                .Select(c => new
                {
                    id = c.Id,
                    name = c.Name
                })
                .ToList();

            var memberGroups = data.Items
                .Where(c => !c.IsAdmin)
                .Select(c => new
                {
                    id = c.Id,
                    name = c.Name
                })
                .ToList();

            return Ok(new
            {
                adminGroups,
                memberGroups
            });
        }


        [HttpGet("opend/dialogs/{userId}")]
        public async Task<IActionResult> OpendDialogs(long userId)
            => Ok(await _tg.GetOpeednChannelsAsync(userId));

        [HttpGet("membersdialogs/{userId}/{groupId}")]
        public async Task<IActionResult> MembersDialogs(long userId, string groupId)
        {
            var members = await _tg.GetGroupMembersAsync(userId, groupId);
            return Ok(members);
        }
        [HttpPost("send-to-members")]
        public async Task<IActionResult> SendToMembers([FromBody] SendTeleMembersRequest request)
        {
            var result = await _tg.SendToMembers(
                request.UserId,
                request.ChatId,
                request.Message
            );

            var logs = new List<MessageLog>();

            // 1️⃣ تسجيل الفاشلين
            if (result.Failures != null && result.Failures.Any())
            {
                foreach (var failure in result.Failures)
                {
                    logs.Add(new MessageLog
                    {
                        MessageId = 0,
                        Recipient = failure.Reason, // هنا غالباً MemberId
                        PlatformId = 2, // رقم Telegram عندك
                        Status = "failed",
                        ErrorMessage = failure.Details,
                        toGroupMember = true,
                        UserId = (int)request.UserId,
                        body = request.Message,
                        sender = "telegram_bot",
                        AttemptedAt = DateTime.UtcNow
                    });
                }
            }

            // 2️⃣ تسجيل الناجحين (عدد فقط)
            int successCount = result.Total - (result.Failures?.Count ?? 0);

            for (int i = 0; i < successCount; i++)
            {
                logs.Add(new MessageLog
                {
                    MessageId = 0,
                    Recipient = "unknown", // لأن معندناش MemberId هنا
                    PlatformId = 2,
                    Status = "sent",
                    toGroupMember = true,
                    UserId = (int)request.UserId,
                    body = request.Message,
                    sender = "telegram_bot",
                    AttemptedAt = DateTime.UtcNow
                });
            }

            _context.message_logs.AddRange(logs);
            await _context.SaveChangesAsync();

            if (result.FloodWait > 0)
                return StatusCode(429, result);

            return Ok(result);
        }

        [HttpPost("send-to-groups")]
        public async Task<IActionResult> SendToGroups([FromBody] SendToGroupsRequest request)
        {
            var result = await _tg.SendToGroups(
                request.UserId,
                request.ChatIds,
                request.Message
            );

            // ✅ نسجل لوج لكل جروب
            foreach (var chatId in request.ChatIds)
            {
                bool success = result.FloodWait == 0 && result.Failed == 0;

                await LogTelegramGroupMessage(
                    request.UserId,
                    chatId,
                    request.Message,
                    success,
                    result.FloodWait > 0 ? $"FloodWait: {result.FloodWait}s" : null
                );
            }

            if (result.FloodWait > 0)
                return StatusCode(429, result);

            return Ok(result);
        }

        [HttpPost("groups/create")]
        public async Task<IActionResult> CreateGroup([FromBody] CreateTelegramGroupRequest request)
        {
            try
            {
                var result = await _tg.CreateGroupAsync(
                    request.UserId,
                    request.Title,
                    request.UserIds
                );

                return Ok(new
                {
                    success = true,
                    groupId = result.Group_Id,
                    title = result.Title
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
        [HttpPost("groups/add-members")]
        public async Task<IActionResult> AddMembersToGroup(
    [FromBody] AddTelegramGroupMembersRequest request)
        {
            try
            {
                await _tg.AddMembersToGroupAsync(
                    request.UserId,
                    request.GroupId,
                    request.UserIds
                );

                return Ok(new
                {
                    success = true,
                    message = "Members added successfully"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        private async Task LogTelegramGroupMessage( long userId,long chatId,string? message, bool success,string? error)
        {
            try
            {
                var log = new MessageLog
                {
                    MessageId = 0, 
                    Recipient = chatId.ToString(),
                    sender = $"{(int)userId}",
                    UserId = (int)userId,
                    body = message,
                    PlatformId = 2,
                    Status = success ? "sent" : "failed",
                    ErrorMessage = error,
                    AttemptedAt = DateTime.Now,
                    ExternalMessageId = null,
                    toGroupMember = false 
                };

                _context.message_logs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch
            {
            }
        }

        [HttpPost("send")]
        public async Task<IActionResult> Send(SendTelegramMessage req)
        {
            await _tg.SendMessageAsync(req.UserId, req.PeerId, req.Message);
            return Ok();
        }
        [HttpGet("groups-and-channels/{userId}")]
        public async Task<IActionResult> GetAllGroupsAndChannels(long userId)
        {
            try
            {
                var data = await _tg.GetGroupsAndChannelsAsync(userId);

                if (data == null)
                    return Ok(new
                    {
                        success = false,
                        message = "Failed to fetch groups and channels"
                    });

                return Ok(new
                {
                    success = true,
                    count = data.Count,
                    items = data.Items
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
        [HttpPost("dialogs/clear")]
        public async Task<IActionResult> ClearDialog([FromBody] ClearDialogRequest request)
        {
            try
            {
                var result = await _tg.ClearDialogAsync(
                    request.UserId,
                    request.Id,
                    request.AccessHash,
                    request.PeerType
                );

                return Ok(new { success = result });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }


        [HttpPost("logout/{userId}")]
        public async Task<IActionResult> Logout(long userId)
        {
            await _tg.LogoutAsync(userId); 
            return Ok(new { success = true });
        }

    }

}
