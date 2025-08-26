using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SuggestionController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SuggestionController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> PostSuggestion([FromBody] SuggestionDto dto)
        {
            if (dto == null) return BadRequest("Invalid body");

            var suggestion = new Suggestion
            {
                suggestionAr = dto.SuggestionAr,
                suggestionEn = dto.SuggestionEn,
                isStarred = dto.IsStarred,
                createdAt = DateTime.UtcNow,
                UserId = dto.UserId,
                updatedAt = DateTime.UtcNow
            };

            _context.Suggestions.Add(suggestion);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Suggestion added successfully", suggestion.Id });
        }
        [HttpGet("read/Suggestions/{userId}")]
        public async Task<IActionResult> GetSuggestions(int userId)
        {
            var list = await _context.Suggestions
                .Where(s => s.UserId == userId)
                .Include(s => s.Replies)
                .Select(s => new SuggestionDto
                {
                    Id = s.Id,
                    SuggestionAr = s.suggestionAr,
                    SuggestionEn = s.suggestionEn,
                   IsStarred = s.isStarred,
                    RepliesDto = s.Replies.Select(r => new SuggestionReplyDto
                    {
                        Id = r.Id,
                        ReplyText = r.ReplyText,
                        UserId = r.UserId
                    }).ToList()
                })
                .ToListAsync();

            return Ok(list);
        }

    }
}
