using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using System.Data;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MessagesController : ControllerBase
    {
        private readonly string _connectionString = "Server=localhost;Database=your_db;Uid=your_user;Pwd=your_password;";

        [HttpPost("send")]
        public IActionResult SendUserMessage([FromBody] SendMessageRequest request)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand("SendUserMessage", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        // إضافة المعاملات
                        cmd.Parameters.AddWithValue("pUserId", request.UserId);
                        cmd.Parameters.AddWithValue("pPlatformId", request.PlatformId);
                        cmd.Parameters.AddWithValue("pTitle", request.Title);
                        cmd.Parameters.AddWithValue("pBody", request.Body);
                        cmd.Parameters.AddWithValue("pChannel", request.Channel);
                        cmd.Parameters.AddWithValue("pSubChannel", request.SubChannel);
                        cmd.Parameters.AddWithValue("pActionType", request.ActionType);
                        cmd.Parameters.AddWithValue("pRecipient", request.Recipient);

                        cmd.ExecuteNonQuery();
                    }
                }

                return Ok(new { status = "success", message = "Message sent successfully" });
            }
            catch (MySqlException ex)
            {
                // التعامل مع أخطاء الـ SQL مثل LimitReached أو FeatureNotFound
                return BadRequest(new { status = "error", message = ex.Message });
            }
        }
    }

    // كلاس لتمرير البيانات من Flutter
    public class SendMessageRequest
    {
        public long UserId { get; set; }
        public int PlatformId { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string Channel { get; set; }
        public string SubChannel { get; set; }
        public string ActionType { get; set; }
        public string Recipient { get; set; }
    }
}
