using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CountriesController : ControllerBase
    {
        private readonly AppDbContext _context;
        public CountriesController(AppDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetCountries()
        {
            var countries = await _context.Countries
                .Where(c => c.IsActive)
                .ToListAsync();
            return Ok(countries);
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class CategoriesController : ControllerBase
    {
        private readonly AppDbContext _context;
        public CategoriesController(AppDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.Categories
                .Where(c => c.IsActive)
                .ToListAsync();
            return Ok(categories);
        }
    }


    [ApiController]
    [Route("api/[controller]")]
    public class CompanyGroupsController : ControllerBase
    {
        private readonly AppDbContext _context;
        public CompanyGroupsController(AppDbContext context) => _context = context;

        [HttpGet("{CategoryID}/{CountryID}")]
        public async Task<IActionResult> GetGroups(int CategoryID, int CountryID)
        {
            var groups = await _context.company_groups
                .Where(g => g.IsActive && !g.IsHidden && g.CountryId == CountryID && g.CategoryId == CategoryID)
                .Include(g => g.OurGroupsCountry)
                .Include(g => g.OurGroupsCategory)
                .Select(g => new CompanyGroup
                {
                    Id = g.Id,
                    GroupName = g.GroupName,
                    InviteLink = g.InviteLink,
                    Description = g.Description,
                    Price = g.Price,
                    IsActive = g.IsActive,
                    IsHidden = g.IsHidden,
                    SendingStatus = g.SendingStatus,
                    CountryNameAr = g.OurGroupsCountry != null ? g.OurGroupsCountry.NameAr : "",
                    CountryNameEn = g.OurGroupsCountry != null ? g.OurGroupsCountry.NameEn : "",
                    CategoryNameAr = g.OurGroupsCategory != null ? g.OurGroupsCategory.NameAr : "",
                    CategoryNameEn = g.OurGroupsCategory != null ? g.OurGroupsCategory.NameEn : ""
                })
                .ToListAsync();

            return Ok(groups);
        }
    }

}
