using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContactsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ContactsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetContacts()
        {
            var socialAccounts = await _context.social_accounts.ToListAsync();
            var directContacts = await _context.direct_contacts.ToListAsync();

            var response = new ContactResponse
            {
                social_accounts = socialAccounts,
                direct_contacts = directContacts
            };

            return Ok(response);
        }

        // ✅ لو حابب تضيف منصات جديدة
        [HttpPost("social")]
        public async Task<IActionResult> AddSocialAccount([FromBody] SocialAccount account)
        {
            _context.social_accounts.Add(account);
            await _context.SaveChangesAsync();
            return Ok(account);
        }

        // ✅ لو حابب تضيف وسيلة اتصال جديدة
        [HttpPost("direct")]
        public async Task<IActionResult> AddDirectContact([FromBody] DirectContact contact)
        {
            _context.direct_contacts.Add(contact);
            await _context.SaveChangesAsync();
            return Ok(contact);
        }
    }
}
