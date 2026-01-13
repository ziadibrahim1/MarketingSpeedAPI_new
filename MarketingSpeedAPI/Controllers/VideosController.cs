using MarketingSpeedAPI.Data;
using Microsoft.AspNet.SignalR.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VideosController : ControllerBase
    {
        private readonly AppDbContext _context;

        public VideosController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetVideos()
        {
            var categories = await _context.video_categories
                .Include(c => c.Videos.Where(v => v.IsActive))
                .ToListAsync();

            var result = categories.Select(c => new
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                NameEN = c.NameEN,
                DescriptionEN = c.DescriptionEN,
                Videos = c.Videos
                .Where(c=>c.IsActive)
                .Select(v => new
                {
                    Id = v.Id,
                    Title = v.Title,
                    Description = v.Description,
                    VideoType = v.VideoType,
                    VideoUrl = v.VideoUrl,
                    FilePath = v.FilePath,
                    Duration = v.Duration
                }).ToList()
            });

            return Ok(result);
        }
    }

}
