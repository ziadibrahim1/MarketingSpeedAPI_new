using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PackageCategoriesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PackageCategoriesController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/PackageCategories
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PackageCategory>>> GetCategories()
        {
            var categories = await _context.PackageCategories
                .Select(c => new
                {
                    c.Id,
                    c.Name ,
                    c.NameEn,
                })
                .ToListAsync();

            return Ok(categories);
        }
    }
}
