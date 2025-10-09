using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GittBilSmsCore.Services; // Ensure this is the correct namespace for SmsReportBackgroundService
using GittBilSmsCore.Data;

namespace GittBilSmsCore.Controllers
{
    public class ReportController : BaseController
    {
        private readonly SmsReportBackgroundService _smsReportBackgroundService;
        private readonly ILogger<ReportController> _logger;

        public ReportController(
            GittBilSmsDbContext context, // ✅ Inject the context
            SmsReportBackgroundService smsReportBackgroundService,
            ILogger<ReportController> logger
        ) : base(context) // ✅ Pass to base controller
        {
            _smsReportBackgroundService = smsReportBackgroundService;
            _logger = logger;
        }

        [HttpGet("/Report/RunManual")]
        public async Task<IActionResult> RunManual()
        {
            _logger.LogInformation("Manual report trigger requested 🚀");

            await _smsReportBackgroundService.RunManualReport();

            _logger.LogInformation("Manual report trigger completed ✅");

            return Ok("Manual report triggered successfully 🚀✅");
        }
    }
}
