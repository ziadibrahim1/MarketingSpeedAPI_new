using Microsoft.AspNetCore.Mvc;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UploadController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;

        public UploadController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpPost]
        public async Task<IActionResult> UploadImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file uploaded" });

            var uploadsPath = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads");

            if (!Directory.Exists(uploadsPath))
                Directory.CreateDirectory(uploadsPath);

            var allowedExts = new[]
            {
                // 🖼️ Images
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg",

                // 🎬 Videos
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".mpeg", ".mpg", ".3gp",

                // 🎵 Audio
            ".mp3", ".wav", ".aac", ".ogg", ".flac", ".wma", ".m4a", ".amr",

                // 📄 Documents
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".rtf",

                // 🗜️ Compressed files ()
            ".zip", ".rar", ".7z"
            };

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExts.Contains(ext))
                return BadRequest(new { message = "Invalid file type" });

            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var fileUrl = $"{baseUrl}/uploads/{fileName}";

            return Ok(new { url = fileUrl });
        }
    }
}
