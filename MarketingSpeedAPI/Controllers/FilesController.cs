using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace MarketingSpeedAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FilesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public FilesController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] long userId)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file uploaded" });

            // تحديد مسار حفظ الملف داخل المشروع
            var uploadsFolder = Path.Combine(_env.ContentRootPath, "Uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var uniqueFileName = $"{Guid.NewGuid()}_{file.FileName}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // حفظ المعلومات في قاعدة البيانات
            var userImage = new UserImage
            {
                UserId = userId,
                FileName = file.FileName,
                FilePath = filePath
            };

            _context.UserImages.Add(userImage);
            await _context.SaveChangesAsync();

            // إرجاع الـ ID
            return Ok(new
            {
                message = "File uploaded successfully",
                imageId = userImage.Id
            });
        }
    }

}
