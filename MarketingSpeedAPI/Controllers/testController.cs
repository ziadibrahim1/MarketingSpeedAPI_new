using MarketingSpeedAPI.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        private readonly IHubContext<ChatHub> _hub;
        public TestController(IHubContext<ChatHub> hub) => _hub = hub;

        [HttpPost("broadcast")]
        public async Task<IActionResult> Broadcast([FromBody] TestMessageDto dto)
        {
            await _hub.Clients.Group(dto.SessionId).SendAsync("ReceiveMessage", dto.UserPhone, dto.Text, DateTime.UtcNow.ToString("o"));
            return Ok(new { sent = true });
        }
    }

    public class TestMessageDto { public string SessionId { get; set; } = ""; public string UserPhone { get; set; } = ""; public string Text { get; set; } = ""; }

}
