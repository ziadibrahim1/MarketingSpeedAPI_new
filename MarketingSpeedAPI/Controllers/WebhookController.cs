using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Hubs;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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
    public async Task<IActionResult> Receive([FromBody] JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messagesArray) ||
            messagesArray.GetArrayLength() == 0)
            return Ok(new { ignored = "no_messages" });

        var messageObj = messagesArray[0];

        bool fromMe = messageObj.GetProperty("from_me").GetBoolean();
        string chatId = messageObj.GetProperty("chat_id").GetString();

        // تجاهل المجموعات أو رسائل النظام
        if (fromMe || chatId.EndsWith("@g.us"))
            return Ok(new { ignored = "group_or_self" });

        string from = messageObj.GetProperty("from").GetString();
        string fromName = messageObj.TryGetProperty("from_name", out var name)
            ? name.GetString()
            : null;

        // تنظيف الرقم
        string cleanNumber = new string(from.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(cleanNumber) ||
            !allowedPrefixes.Any(p => cleanNumber.StartsWith(p)))
            return Ok(new { ignored = "invalid_prefix" });

        string text = ExtractWhapiMessageText(messageObj);

        if (string.IsNullOrWhiteSpace(text))
            return Ok(new { ignored = "empty_text" });

        string channelId = root.GetProperty("channel_id").GetString();

        var account = await _db.user_accounts
            .FirstOrDefaultAsync(a => a.channelId == channelId && a.Status == "connected");

        if (account == null)
            return Ok(new { ignored = "no_matching_account" });

        var msg = new ChatMessage
        {
            MessageId = messageObj.GetProperty("id").GetString(),
            UserPhone = cleanNumber,
            Text = text,
            IsSentByMe = false,
            Timestamp = DateTime.Now,
            IsRaeded = false,
            channelId = account.channelId,
            reciverNumber = account.AccountIdentifier,
            ContactName = fromName
        };

        _db.ChatMessages.Add(msg);
        await _db.SaveChangesAsync();

        await _hub.Clients.Group($"session_{account.channelId}")
            .SendAsync("ReceiveMessage",
                msg.UserPhone,
                msg.Text,
                msg.Timestamp.ToString("o"),
                null,
                fromName);

        return Ok(new { success = true });
    }

    private string ExtractWhapiMessageText(JsonElement msg)
    {
        string type = msg.GetProperty("type").GetString();

        switch (type)
        {
            case "text":
                return msg.GetProperty("text").GetProperty("body").GetString();

            case "link_preview":
                return msg.GetProperty("link_preview").GetProperty("body").GetString();

            case "image":
                if (msg.TryGetProperty("image", out var img) && img.TryGetProperty("caption", out var c))
                    return c.GetString();
                break;

            case "video":
                if (msg.TryGetProperty("video", out var vid) && vid.TryGetProperty("caption", out var c2))
                    return c2.GetString();
                break;
        }

        return "";
    }
}
