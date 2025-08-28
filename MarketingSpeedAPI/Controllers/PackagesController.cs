using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PackagesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PackagesController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Package>>> GetAll()
        {
            var packages = await _context.Packages
     .Include(p => p.Features)
     .Select(p => new PackageDto
     {
         Id = p.Id,
         Name = p.Name,
         Price = (double)p.Price,
         DurationDays = p.DurationDays,
         Discount = (double)p.Discount,
         Features = p.Features.Select(f => f.Feature).ToList(),
         SubscriberCount = p.SubscriberCount
     }).ToListAsync();

            return Ok(packages);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Package>> GetById(long id)
        {
            var pkg = await _context.Packages
                .Include(p => p.Features)
                .Include(p => p.Logs)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (pkg == null) return NotFound();
            return pkg;
        }

        [HttpPost]
        public async Task<ActionResult<Package>> Create(Package package)
        {
            _context.Packages.Add(package);
            await _context.SaveChangesAsync();

            // log
            _context.PackageLogs.Add(new PackageLog
            {
                PackageId = package.Id,
                UserId = 1, // TODO: استبدلها بالـ UserId من الـ JWT أو السياق
                Action = "create",
                Description = $"Package {package.Name} created"
            });
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = package.Id }, package);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(long id, Package package)
        {
            if (id != package.Id) return BadRequest();

            _context.Entry(package).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            // log
            _context.PackageLogs.Add(new PackageLog
            {
                PackageId = package.Id,
                UserId = 1,
                Action = "update",
                Description = $"Package {package.Name} updated"
            });
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Archive(long id)
        {
            var pkg = await _context.Packages.FindAsync(id);
            if (pkg == null) return NotFound();

            pkg.Archived = true;
            await _context.SaveChangesAsync();

            _context.PackageLogs.Add(new PackageLog
            {
                PackageId = pkg.Id,
                UserId = 1,
                Action = "archive",
                Description = $"Package {pkg.Name} archived"
            });
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // 📌 إحصائية: أكثر الباقات استخداماً (Top Packages by Subscribers)
        [HttpGet("top/{count}")]
        public async Task<ActionResult<IEnumerable<object>>> GetTopPackages(int count = 5)
        {
            var top = await _context.Packages
                .Where(p => !p.Archived && p.Status == "active")
                .OrderByDescending(p => p.SubscriberCount)
                .Take(count)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.SubscriberCount,
                    p.Price,
                    p.DurationDays
                })
                .ToListAsync();

            return Ok(top);
        }

        // 📌 إحصائية: الباقات التي لا تحتوي مشتركين منذ أكثر من 30 يوم
        [HttpGet("inactive-over-30-days")]
        public async Task<ActionResult<IEnumerable<object>>> GetInactivePackages()
        {
            var dateThreshold = DateTime.UtcNow.AddDays(-30);

            var inactive = await _context.Packages
                .Where(p => p.SubscriberCount == 0 && p.UpdatedAt < dateThreshold && !p.Archived)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.UpdatedAt,
                    p.Status
                })
                .ToListAsync();

            return Ok(inactive);
        }

        // 📌 إحصائية: عدد الباقات حسب الحالة (active / inactive / archived)
        [HttpGet("stats/status")]
        public async Task<ActionResult<object>> GetPackageStatusStats()
        {
            var stats = await _context.Packages
                .GroupBy(p => p.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .ToListAsync();

            var archivedCount = await _context.Packages.CountAsync(p => p.Archived);

            return Ok(new { Stats = stats, Archived = archivedCount });
        }

        // 📌 إحصائية: مجموع إيرادات الباقات (على السعر × عدد المشتركين)
        [HttpGet("stats/revenue")]
        public async Task<ActionResult<object>> GetRevenue()
        {
            var revenue = await _context.Packages
                .Where(p => !p.Archived)
                .SumAsync(p => p.Price * p.SubscriberCount);

            return Ok(new { TotalRevenue = revenue });
        }

        // 📌 إحصائية: الباقات المجدولة للنشر في المستقبل
        [HttpGet("scheduled")]
        public async Task<ActionResult<IEnumerable<object>>> GetScheduledPackages()
        {
            var scheduled = await _context.Packages
                .Where(p => p.ScheduledAt != null && p.ScheduledAt > DateTime.UtcNow)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.ScheduledAt,
                    p.Price,
                    p.DurationDays
                })
                .ToListAsync();

            return Ok(scheduled);
        }

    }
}
