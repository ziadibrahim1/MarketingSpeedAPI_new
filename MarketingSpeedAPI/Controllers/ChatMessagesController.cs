using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
                    chatMessages = g.OrderBy(m => m.Timestamp).Select(m => new
                    {
                        text = m.Text,
                        timestamp = m.Timestamp,
                        isSentByMe = m.IsSentByMe
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

            return Ok(messages);
        }

        [HttpPost]
        public async Task<IActionResult> AddChatMessage([FromBody] ChatMessage message)
        {
            message.Timestamp = DateTime.UtcNow;
            _context.ChatMessages.Add(message);
            await _context.SaveChangesAsync();
            return Ok(message);
        }


       

    }

}
