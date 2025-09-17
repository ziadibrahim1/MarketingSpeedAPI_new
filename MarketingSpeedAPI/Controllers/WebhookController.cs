using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Hubs;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<ChatHub> _hub;

        public WebhookController(AppDbContext db, IHubContext<ChatHub> hubContext)
        {
            _db = db;
            _hub = hubContext;
        }

        [HttpPost]
        public async Task<IActionResult> Receive()
        {
            string body;
            using (var reader = new StreamReader(Request.Body))
            {
                body = await reader.ReadToEndAsync();
            }

            var headerSig = Request.Headers["x-webhook-signature"].FirstOrDefault();
            if (string.IsNullOrEmpty(headerSig))
                return Unauthorized(new { error = "Missing signature" });

            var accounts = _db.user_accounts
                .Where(a => a.WebhookSecret != null)
                .ToList();

            UserAccount? matchedAccount = null;

            foreach (var account in accounts)
            {
                if (headerSig == account.WebhookSecret)
                {
                    matchedAccount = account;
                    break;
                }
            }

            if (matchedAccount == null)
                return Unauthorized(new { error = "Invalid signature" });

            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("event", out var eventElement))
                return Ok(new { ignored = "no event" });

            var eventType = eventElement.GetString();

            if (eventType == "messages.received" || eventType == "messages.upsert" || eventType == "message.sent")
            {
                if (!root.TryGetProperty("data", out var data))
                    return Ok(new { ignored = "no data" });

                JsonElement key;
                JsonElement message;
                string remoteJid;

                if (data.TryGetProperty("key", out key) && data.TryGetProperty("message", out message))
                {
                    remoteJid = key.GetProperty("remoteJid").GetString();
                }
                else if (data.TryGetProperty("messages", out var messagesElement))
                {
                    if (messagesElement.ValueKind == JsonValueKind.Object)
                    {
                        if (!messagesElement.TryGetProperty("key", out key))
                            return Ok(new { ignored = "no key in messages" });

                        messagesElement.TryGetProperty("message", out message); 
                        remoteJid = key.GetProperty("remoteJid").GetString();
                    }
                    else if (messagesElement.ValueKind == JsonValueKind.Array)
                    {
                        var firstMsg = messagesElement[0];
                        if (!firstMsg.TryGetProperty("key", out key))
                            return Ok(new { ignored = "no key in messages array" });

                        firstMsg.TryGetProperty("message", out message);
                        remoteJid = key.GetProperty("remoteJid").GetString();
                    }
                    else
                    {
                        return Ok(new { ignored = "messages not object/array" });
                    }
                }
                else
                {
                    return Ok(new { ignored = "no key/message" });
                }

                if (remoteJid.EndsWith("@g.us"))
                    return Ok(new { ignored = "group message" });

                var msg = new ChatMessage
                {
                    MessageId = key.GetProperty("id").GetString(),
                    UserPhone = remoteJid.Split('@')[0],
                    Text = ExtractMessageText(message),
                    IsSentByMe = key.GetProperty("fromMe").GetBoolean(),
                    Timestamp = DateTime.UtcNow,
                    SessionId = matchedAccount.WasenderSessionId ?? 0
                };

                _db.ChatMessages.Add(msg);
                await _db.SaveChangesAsync();

                await _hub.Clients.Group($"session_{matchedAccount.WasenderSessionId}")
                    .SendAsync("ReceiveMessage",
                        msg.UserPhone,
                        msg.Text,
                        msg.Timestamp.ToString("o"));
            }


            return Ok(new { received = true });
        }
        private string ExtractMessageText(JsonElement message)
        {
            if (message.ValueKind != JsonValueKind.Object)
                return "";

            if (message.TryGetProperty("conversation", out var conv))
                return conv.GetString();

            if (message.TryGetProperty("extendedTextMessage", out var ext))
            {
                if (ext.TryGetProperty("text", out var extText))
                    return extText.GetString();
            }

            if (message.TryGetProperty("imageMessage", out var img))
            {
                if (img.TryGetProperty("caption", out var caption))
                    return caption.GetString();
            }

            if (message.TryGetProperty("videoMessage", out var vid))
            {
                if (vid.TryGetProperty("caption", out var caption))
                    return caption.GetString();
            }

            return "";
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] ChatMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.UserPhone) || string.IsNullOrWhiteSpace(message.Text))
                return BadRequest(new { error = "UserPhone and Text are required" });

            message.IsSentByMe = true;
            message.Timestamp = DateTime.UtcNow;

            _db.ChatMessages.Add(message);
            await _db.SaveChangesAsync();
            return Ok(message);
        }
    }
}
