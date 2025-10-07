using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Hubs;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestSharp;
using System.Text.Json;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<ChatHub> _hub;

        private readonly List<string> allowedPrefixes = new()
        {
            "20","966","971","974","973","965","968","967","962","961","963","964","970",
            "249","218","213","212","216","222","252","253","269"
        };

        public WebhookController(AppDbContext db, IHubContext<ChatHub> hub)
        {
            _db = db;
            _hub = hub;
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
                return Ok(new { status = "webhook endpoint alive" });

            var matchedAccount = await _db.user_accounts
                .Where(a => a.WebhookSecret != null && a.WebhookSecret == headerSig && a.Status == "connected")
                .FirstOrDefaultAsync();

            if (matchedAccount == null)
                return Unauthorized(new { error = "Invalid signature or no connected account" });

            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("event", out var eventElement))
                return Ok(new { ignored = "no event" });

            if (eventElement.GetString() != "messages-personal.received")
                return Ok(new { ignored = "not a personal message" });

            if (!root.TryGetProperty("data", out var data))
                return Ok(new { ignored = "no data" });

         
            JsonElement key, message;
            string remoteJid;

            if (data.TryGetProperty("messages", out var messagesElement))
            {
                if (messagesElement.ValueKind == JsonValueKind.Object)
                {
                   
                    messagesElement.TryGetProperty("key", out key);
                    messagesElement.TryGetProperty("message", out message);
                    remoteJid = key.GetProperty("remoteJid").GetString();
                }
                else if (messagesElement.ValueKind == JsonValueKind.Array)
                {
                    var firstMsg = messagesElement[0];
                    firstMsg.TryGetProperty("key", out key);
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


            // تجاهل رسائل المجموعات
            if (remoteJid.EndsWith("@g.us"))
                return Ok(new { ignored = "group message" });

            // تنظيف الرقم والتحقق من البريفكس
            var phonePart = remoteJid.Split('@')[0];
            var cleanNumber = new string(phonePart.Where(char.IsDigit).ToArray());

            if (string.IsNullOrEmpty(cleanNumber) || !allowedPrefixes.Any(p => cleanNumber.StartsWith(p)))
                return Ok(new { ignored = "invalid number prefix" });

            var msg = new ChatMessage
            {
                MessageId = key.GetProperty("id").GetString(),
                UserPhone = cleanNumber,
                Text = ExtractMessageText(message),
                IsSentByMe = key.GetProperty("fromMe").GetBoolean(),
                Timestamp = DateTime.UtcNow,
                IsRaeded = false,
                SessionId = matchedAccount.WasenderSessionId ?? 0,
                reciverNumber = matchedAccount.AccountIdentifier
            };

            _db.ChatMessages.Add(msg);
            await _db.SaveChangesAsync();
            var ImClient = new RestClient("https://www.wasenderapi.com");
            var ImRequestContact = new RestRequest($"/api/contacts/{cleanNumber}/picture", Method.Get);
            ImRequestContact.AddHeader("Authorization", $"Bearer {matchedAccount.AccessToken}");
            var ImContactResponse = await ImClient.ExecuteAsync(ImRequestContact);


            var client = new RestClient("https://www.wasenderapi.com");
            var requestContact = new RestRequest($"/api/contacts/{cleanNumber}", Method.Get);
            requestContact.AddHeader("Authorization", $"Bearer {matchedAccount.AccessToken}");

            var contactResponse = await client.ExecuteAsync(requestContact);

            string? imgUrl = null;
            string? contactName = null;

            if (contactResponse.IsSuccessful)
            {
                try
                {
                    var contactJson = JsonDocument.Parse(contactResponse.Content);
                    if (contactJson.RootElement.TryGetProperty("data", out var dataContact))
                    {
                        
                        contactName = dataContact.GetProperty("notify").GetString();

                        msg.ContactName = contactName;

                        if (ImContactResponse.IsSuccessful)
                        {
                            var ImContactJson = JsonDocument.Parse(ImContactResponse.Content);
                            if (ImContactJson.RootElement.TryGetProperty("data", out var ImDataContact))
                            {
                                imgUrl = ImDataContact.GetProperty("imgUrl").GetString();
                                msg.ProfileImageUrl = imgUrl;
                            }
                               
                        }

                        _db.ChatMessages.Update(msg);
                        await _db.SaveChangesAsync();
                    }
                }
                catch { }
            }

            
            await _hub.Clients.Group($"session_{matchedAccount.WasenderSessionId}")
                .SendAsync("ReceiveMessage",
                    msg.UserPhone,
                    msg.Text,
                    msg.Timestamp.ToString("o"),
                    imgUrl,
                    contactName);

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
            message.IsRaeded = false;
            message.Timestamp = DateTime.UtcNow;

            _db.ChatMessages.Add(message);
            await _db.SaveChangesAsync();
            return Ok(message);
        }
    }
}
