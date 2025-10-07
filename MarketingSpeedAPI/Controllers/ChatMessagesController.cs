using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Windows.ApplicationModel.Contacts;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatMessagesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ChatMessagesController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetChatMessages()
        {
            var chats = await _context.ChatMessages
                .GroupBy(m => m.UserPhone)
                .Select(g => new
                {
                    userPhone = g.Key,
                    contactName = g.FirstOrDefault().ContactName,        
                    profileImageUrl = g.FirstOrDefault().ProfileImageUrl,  
                    chatMessages = g.OrderBy(m => m.Timestamp).Select(m => new
                    {
                        text = m.Text,
                        timestamp = m.Timestamp,
                        IsReaded = m.IsRaeded,
                        isSentByMe = m.IsSentByMe,
                    }).ToList()
                }).ToListAsync();

            return Ok(chats);
        }

        [HttpGet("{userPhone}")]
        public async Task<IActionResult> GetChatMessagesForUser(string userPhone)
        {
            var messages = await _context.ChatMessages
                .Where(m => m.UserPhone == userPhone)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

            if (!messages.Any())
                return NotFound();

            foreach (var message in messages)
            {
                message.IsRaeded = true;
            }
            await _context.SaveChangesAsync();
            return Ok(messages);
        }

        [HttpDelete("{userPhone}")]
        public async Task<IActionResult> DeleteChatMessagesForUser(string userPhone)
        {
            var messages = await _context.ChatMessages
                .Where(m => m.UserPhone == userPhone)
                .ToListAsync();

            if (!messages.Any())
                return NotFound(new { message = "لا توجد محادثات لحذفها" });

            _context.ChatMessages.RemoveRange(messages);
            await _context.SaveChangesAsync();

            return Ok(new { message = "تم حذف المحادثة بنجاح" });
        }

        [HttpPost("{AddChatMessage}")]
        public async Task<IActionResult> AddChatMessage([FromBody] ChatMessage message)
        {
            message.Timestamp = DateTime.UtcNow;
            _context.ChatMessages.Add(message);
            await _context.SaveChangesAsync();
            return Ok(message);
        }


       

    }

}
