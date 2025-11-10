using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using RestSharp;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class paymentsController : Controller
    {
        [HttpPost("create-session")]
        public async Task<IActionResult> CreatePayment([FromBody] PaymentRequest req)
        {
            try
            {
                var options = new RestClientOptions("https://api.moyasar.com/v1/payments")
                {
                    MaxTimeout = -1,
                };
                var client = new RestClient(options);

                var request = new RestRequest("", Method.Post);
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Accept", "application/json");

                // ⚠️ استخدم مفاتيحك الحقيقية من حساب ميسر
                string moyasarApiKey = "sk_test_xxxxxxxxxxxxxxxxx"; // المفتاح السري

                // ⚙️ بيانات الدفع
                var paymentData = new
                {
                    given_id = Guid.NewGuid().ToString(),
                    amount = (int)(req.Amount * 100), // بالهللة
                    currency = "SAR",
                    description = $"Subscription for package {req.PackageId}",
                    callback_url = "https://marketingspeed.online/payment/success",
                    source = new
                    {
                        type = "creditcard",
                        name = req.CardName ?? "Customer",
                        number = req.CardNumber ?? "4111111111111111",
                        month = req.Month,
                        year = req.Year,
                        cvc = req.Cvc,
                        statement_descriptor = "MarketingSpeed",
                        _3ds = true,
                        manual = false,
                        save_card = false
                    },
                    metadata = new
                    {
                        customer_id = req.UserId.ToString(),
                        package_id = req.PackageId.ToString(),
                        coupon = req.Coupon
                    },
                    apply_coupon = !string.IsNullOrEmpty(req.Coupon)
                };

                request.AddStringBody(JsonConvert.SerializeObject(paymentData), DataFormat.Json);
                request.AddHeader("Authorization", "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{moyasarApiKey}:")));

                var response = await client.ExecuteAsync(request);
                return Content(response.Content ?? "{}", "application/json");
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        

    }
}
