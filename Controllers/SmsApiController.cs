using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using System.Text;
using Newtonsoft.Json;
using Serilog;
using Microsoft.Extensions.Logging;
using GittBilSmsCore.Helpers;
using Microsoft.Extensions.Localization;
using CsvHelper;
namespace GittBilSmsCore.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SmsApiController : ControllerBase
    {
        private readonly GittBilSmsDbContext _context;
        private readonly IStringLocalizer _sharedLocalizer;

        private readonly Microsoft.Extensions.Logging.ILogger _smsLogger;

        public SmsApiController(GittBilSmsDbContext context, IStringLocalizerFactory factory, ILogger<SmsApiController> smsLogger)
        {
            _context = context;
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
            // Create a special logger for SMS
            _smsLogger = smsLogger;
        }



        [HttpPost("SendSms")]
        public async Task<IActionResult> SendSms([FromBody] SendSmsApiRequest request)
        {
            _smsLogger.LogInformation("SMS start: coming");
            // 🚀 1️⃣ Authenticate client
            var api = await _context.Apis
           .Where(a => a.Username == request.Username && a.Password == request.Password && a.IsActive)
           .FirstOrDefaultAsync();

            if (api == null)
            {
                return Unauthorized(new { status = "FAILED", error = "Invalid username or password" });
            }
            _smsLogger.LogInformation("SendSms API called: Username={Username}, Password={Password}, Message={Message}, PhoneNumbers={Count}",
    request.Username, request.Password, request.Message, request.PhoneNumbers);
            var company = await _context.Companies
                .FirstOrDefaultAsync(c => c.Apid == api.ApiId && c.IsActive);

            if (company == null)
            {
                return Unauthorized(new { status = "FAILED", error = "Company not linked to this API" });
            }

            // 🚀 3️⃣ Validate numbers
            var validNumbers = new List<string>();
            foreach (var number in request.PhoneNumbers ?? new List<string>())
            {
                var cleaned = number.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Trim();

                if (cleaned.StartsWith("+90")) cleaned = cleaned.Substring(1);

                if ((cleaned.StartsWith("90") && cleaned.Length == 12) ||
                    (cleaned.StartsWith("05") && cleaned.Length == 11) ||
                    (cleaned.StartsWith("5") && cleaned.Length == 10))
                {
                    validNumbers.Add(cleaned);
                }
            }

            if (validNumbers.Count == 0)
            {
                return BadRequest(new { status = "FAILED", error = "No valid recipients." });
            }

            // 🚀 4️⃣ Check balance
            var pricePerSms = 0.05m; // Example price
            var totalCost = validNumbers.Count * pricePerSms;
            var availableBalance =  company.CreditLimit;

            if (availableBalance < totalCost)
            {
                return BadRequest(new { status = "FAILED", error = "Insufficient balance." });
            }

            // 🚀 5️⃣ Deduct balance
            company.CreditLimit -= totalCost;

            // 🚀 6️⃣ Create Order
            var order = new Order
            {
                CompanyId = company.CompanyId,
                ApiId = api.ApiId,
                SubmissionType = "API",
                ScheduledSendDate = TimeHelper.NowInTurkey(),
                MessageText = request.Message,
                LoadedCount = validNumbers.Count,
                ProcessedCount = 0,
                UnsuccessfulCount = 0,
                Refundable = false,
                Returned = false,
                CurrentStatus = "WaitingToBeSent",
                CreatedByUserId = 0, // System API call
                CreatedAt = TimeHelper.NowInTurkey(),
                PricePerSms = pricePerSms,
                TotalPrice = totalCost
            };
            _smsLogger.LogInformation("SMS order", order);
            _context.Orders.Add(order);

            // 🚀 7️⃣ Save BalanceHistory
            _context.BalanceHistory.Add(new BalanceHistory
            {
                CompanyId = company.CompanyId,
                Amount = -totalCost,
                Action = "Deduct on Send (API)",
                CreatedAt = TimeHelper.NowInTurkey(),
                CreatedByUserId = 0,
                OrderId = order.OrderId,
            });

            _context.CreditTransactions.Add(new CreditTransaction
            {
                CompanyId = company.CompanyId,
                TransactionType = _sharedLocalizer["Order_Payment"],
                Credit = -totalCost,
                Currency = "TRY",
                TransactionDate = TimeHelper.NowInTurkey(),
                Note = $"SMS Order #{order.OrderId} - API",
                UnitPrice = pricePerSms,
                TotalPrice = totalCost
            });

           
            await _context.SaveChangesAsync();

            // 🚀 8️⃣ Build Yurtici format
            var payload = new
            {
                Username = "Gittibil",
                Password = "FEı4*Ld",
                Text = request.Message,
                To = validNumbers
            };
            _smsLogger.LogInformation("SMS body", payload);
            var requestBody = JsonConvert.SerializeObject(payload);

            using var client = new HttpClient();
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, api.ApiUrl)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, api.ContentType)
            };
            _smsLogger.LogInformation("SMS url: Url={Url}, Username={Username}, Password={Password}, Message={Message}, PhoneNumbers={PhoneNumbers}",
     api.ApiUrl, api.Username, api.Password, request.Message, validNumbers);
            // 🚀 9️⃣ Send to Yurtici
            var response = await client.SendAsync(httpRequest);
            var result = await response.Content.ReadAsStringAsync();
            _smsLogger.LogInformation("SMS API Response: StatusCode={StatusCode}, Response={Response}",
    response.StatusCode, result);

            if (!response.IsSuccessStatusCode)
            {
                _smsLogger.LogInformation("SMS API FAILED: StatusCode={StatusCode}, Response={Response}",
      response.StatusCode, result);
                // 🚀 Refund
                company.CreditLimit += totalCost;

                order.CurrentStatus = "Failed";
                order.ApiErrorResponse = $"HTTP {(int)response.StatusCode} - {result}";

                _context.BalanceHistory.Add(new BalanceHistory
                {
                    CompanyId = company.CompanyId,
                    Amount = totalCost,
                    Action = "Refund on Failed",
                    CreatedAt = TimeHelper.NowInTurkey(),
                    CreatedByUserId = 0,
                    OrderId = order.OrderId,
                });
                // ✅ Track refund in CreditTransactions
                _context.CreditTransactions.Add(new CreditTransaction
                {
                    CompanyId = company.CompanyId,
                    TransactionType = _sharedLocalizer["Order_Cancellation"],
                    Credit = totalCost,
                    Currency = "TRY",
                    TransactionDate = TimeHelper.NowInTurkey(),
                    Note = $"Sipariş iadesi (API Failed - HTTP Error) - Order #{order.OrderId}",
                    UnitPrice = 0,
                    TotalPrice = 0
                });


                await _context.SaveChangesAsync();

                return StatusCode((int)response.StatusCode, new { status = "FAILED", error = $"SMS API failed: {result}" });
            }

            // 🚀 10️⃣ Check Yurtici response
            dynamic json = JsonConvert.DeserializeObject(result);

            if (json.Status == "OK")
            {
                order.SmsOrderId = json.MessageId;
                order.CurrentStatus = "Sent";
                order.StartedAt = TimeHelper.NowInTurkey();
                order.CompletedAt = TimeHelper.NowInTurkey();
                order.ScheduledSendDate = TimeHelper.NowInTurkey();
                order.ProcessedCount = validNumbers.Count;

                await _context.SaveChangesAsync();

                return Ok(new { status = "SUCCESS", message = "SMS sent successfully", messageId = order.SmsOrderId });
            }
            else
            {
                // 🚀 Refund
                company.CreditLimit += totalCost;

                order.CurrentStatus = "Failed";
                order.ApiErrorResponse = $"Status: {json.Status}, Full Response: {result}";

                _context.BalanceHistory.Add(new BalanceHistory
                {
                    CompanyId = company.CompanyId,
                    Amount = totalCost,
                    Action = "Refund on Failed",
                    CreatedAt = TimeHelper.NowInTurkey(),
                    CreatedByUserId = 0,
                    OrderId = order.OrderId,
                });
                // ✅ Track refund in CreditTransactions
                _context.CreditTransactions.Add(new CreditTransaction
                {
                    CompanyId = company.CompanyId,
                    TransactionType = _sharedLocalizer["Order_Cancellation"],
                    Credit = totalCost,
                    Currency = "TRY",
                    TransactionDate = TimeHelper.NowInTurkey(),
                    Note = $"Sipariş iadesi (API Status Error) - Order #{order.OrderId}",
                    UnitPrice = 0,
                    TotalPrice = 0
                });
                await _context.SaveChangesAsync();

                return BadRequest(new { status = "FAILED", error = $"SMS API returned an error: {json.Status}" });
            }
        }
    }

    // 🚀 Request Model
    public class SendSmsApiRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string Message { get; set; }
        public List<string> PhoneNumbers { get; set; }
    }
}