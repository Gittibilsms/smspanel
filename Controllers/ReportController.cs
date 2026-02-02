using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GittBilSmsCore.Services;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Linq;
using System;
using System.Collections.Generic;

namespace GittBilSmsCore.Controllers
{
    public class ReportController : BaseController
    {
        private readonly SmsReportBackgroundService _smsReportBackgroundService;
        private readonly ILogger<ReportController> _logger;

        public ReportController(
            GittBilSmsDbContext context,
            SmsReportBackgroundService smsReportBackgroundService,
            ILogger<ReportController> logger
        ) : base(context)
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

        [HttpGet("/Report/RunSingleOrder")]
        public async Task<IActionResult> RunSingleOrder(int orderId, bool forceRefund = true)
        {
            try
            {
                orderId = 56820;
                _logger.LogInformation($"Manual single order report requested for OrderId={orderId}");

                using (var scope = HttpContext.RequestServices.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<GittBilSmsDbContext>();
                    var telegramService = scope.ServiceProvider.GetRequiredService<TelegramMessageService>();
                    var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                    var localizerFactory = scope.ServiceProvider.GetRequiredService<IStringLocalizerFactory>();
                    var sharedLocalizer = localizerFactory.Create("SharedResource", "GittBilSmsCore");

                    var supportedApis = new[] { "yurtici", "telsim", "turkcell" };

                    var order = await dbContext.Orders
                        .Include(o => o.Company)
                        .Include(o => o.Api)
                        .FirstOrDefaultAsync(o => o.OrderId == orderId
                                                && o.SmsOrderId != null
                                                && supportedApis.Contains(o.Api.ServiceName.ToLower()));

                    if (order == null)
                    {
                        return Json(new { success = false, message = "Order not found or not supported for reporting." });
                    }

                    _logger.LogInformation($"Processing report for OrderId={orderId}");

                    var httpClient = httpClientFactory.CreateClient();
                    var apiUrl = order.Api.ApiUrl;
                    var username = order.Api.Username;
                    var password = order.Api.Password;
                    var contentType = order.Api.ContentType?.ToLower() ?? "application/json";

                    HttpContent content;

                    if (contentType == "application/json")
                    {
                        var payload = new
                        {
                            Username = username,
                            Password = password,
                            MessageId = order.SmsOrderId
                        };

                        var json = JsonSerializer.Serialize(payload);
                        content = new StringContent(json, Encoding.UTF8, "application/json");
                    }
                    else if (contentType == "text/xml")
                    {
                        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<ReportRequest>
    <Username>{username}</Username>
    <Password>{password}</Password>
    <OrderId>{order.SmsOrderId}</OrderId>
    <Chunks></Chunks>
</ReportRequest>";
                        content = new StringContent(xml, Encoding.UTF8, "text/xml");
                    }
                    else
                    {
                        return Json(new { success = false, message = $"Unsupported ContentType: {contentType}" });
                    }

                    // Call API
                    var response = await httpClient.PostAsync(apiUrl, content);
                    response.EnsureSuccessStatusCode();

                    var responseBody = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation($"API Response: {responseBody}");

                    // Parse response
                    var smsReportResponse = JsonSerializer.Deserialize<YurticiSmsReportResponse>(responseBody);

                    if (smsReportResponse?.Response?.Report?.List != null)
                    {
                        var list = smsReportResponse.Response.Report.List;

                        // Save counts
                        order.ProcessedCount = list.Count;
                        order.DeliveredCount = list.Count(x => x.State == "İletildi");
                        order.UndeliveredCount = list.Count(x => x.State == "İletilmedi");
                        order.UnsuccessfulCount = list.Count(x => x.State == "İletilmedi");
                        order.WaitingCount = list.Count(x => x.State == "Beklemede" || x.State == "Waiting");
                        order.ExpiredCount = list.Count(x => x.State == "Süre Aşımı" || x.State == "Zaman Aşımı" || x.State == "Expired");

                        // Check if 24h passed OR force refund
                        bool isFinal = forceRefund ||
                                      (order.StartedAt.HasValue &&
                                       (DateTime.UtcNow.AddHours(3) - order.StartedAt.Value).TotalHours >= 24);

                        if (isFinal)
                        {
                            order.ReportLock = false;
                            order.ReportedAt = DateTime.UtcNow.AddHours(3);
                            _logger.LogInformation($"FINAL report for OrderId={order.OrderId}");

                            // 🚀 REFUND LOGIC
                            if (order.Company.IsRefundable && order.Refundable && !order.Returned)
                            {
                                var segmentsPerMessage = order.SmsCount > 0 ? order.SmsCount : 1;
                                var refundableCount = (order.ExpiredCount ?? 0) + (order.UndeliveredCount ?? 0);

                                if (refundableCount > 0)
                                {
                                    var refundAmount = refundableCount * segmentsPerMessage;

                                    // 1️⃣ Add to Company CreditLimit
                                    order.Company.CreditLimit += refundAmount;
                                    order.RefundAmount = refundAmount;

                                    // 2️⃣ Create BalanceHistory entry
                                    dbContext.BalanceHistory.Add(new BalanceHistory
                                    {
                                        CompanyId = order.CompanyId,
                                        Amount = (decimal)refundAmount,
                                        Action = "Manual Refund on Report",
                                        OrderId = order.OrderId,
                                        CreatedAt = DateTime.UtcNow.AddHours(3),
                                        CreatedByUserId = HttpContext.Session.GetInt32("UserId")
                                    });

                                    // 3️⃣ Create CreditTransaction entry
                                    dbContext.CreditTransactions.Add(new CreditTransaction
                                    {
                                        CompanyId = order.CompanyId,
                                        TransactionType = sharedLocalizer["creditrefunded"],
                                        Credit = (decimal)refundAmount,
                                        TransactionDate = DateTime.UtcNow.AddHours(3),
                                        Note = $"Sipariş iadesi (Manuel) - Order #{order.OrderId}",
                                        UnitPrice = 0,
                                        TotalPrice = 0,
                                        Currency = "TRY"
                                    });

                                    order.Returned = true;
                                    order.ReturnDate = DateTime.UtcNow.AddHours(3);
                                    await dbContext.SaveChangesAsync();
                                    // 4️⃣ Send Telegram notification
                                    decimal? availableCredit = await (
                                        from c in dbContext.Companies
                                        join u in dbContext.Users on c.CompanyId equals u.CompanyId
                                        where u.IsMainUser == true && c.CompanyId == order.CompanyId
                                        select (decimal?)c.CreditLimit
                                    ).FirstOrDefaultAsync();

                                    var textMsg = string.Format(
                                        sharedLocalizer["CreditRefundedMessage"],
                                        (decimal)refundAmount,
                                        availableCredit
                                    );

                                    string? companyName = await (
                                        from c in dbContext.Companies
                                        join u in dbContext.Users on c.CompanyId equals u.CompanyId
                                        where u.IsMainUser == true && c.CompanyId == order.CompanyId
                                        select (string?)c.CompanyName
                                    ).FirstOrDefaultAsync();

                                    var textMsgtoAdmin = string.Format(
                                        sharedLocalizer["CreditRefundedMessageToAdmin"],
                                        companyName,
                                        (decimal)refundAmount,
                                        availableCredit
                                    );

                                    await telegramService.SendToUsersAsync(
                                        order.CompanyId,
                                        HttpContext.Session.GetInt32("UserId") ?? 0,
                                        textMsg,
                                        "Manual refund triggered",
                                        textMsgtoAdmin,
                                        0
                                    );

                                    _logger.LogInformation($"✅ Refunded {refundAmount} credits to CompanyId={order.CompanyId}");
                                }
                            }
                        }

                      

                        // Save CSV files
                        var orderFolderPath = Path.Combine("D:\\home\\data", "orders", order.OrderId.ToString());
                        Directory.CreateDirectory(orderFolderPath);

                        // Summary CSV
                        var summaryCsv = new StringBuilder();
                        summaryCsv.AppendLine($"\"Gönderim Tarihi\";\"{order.StartedAt?.ToString("yyyy-MM-ddTHH:mm:ss.000Z")}\"");
                        summaryCsv.AppendLine($"\"Rapor Tarihi\";\"{DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-ddTHH:mm:ss.000Z")}\"");
                        summaryCsv.AppendLine($"\"Toplam İşlenen\";\"{order.ProcessedCount}\"");
                        summaryCsv.AppendLine($"\"İletildi\";\"{order.DeliveredCount}\"");
                        summaryCsv.AppendLine($"\"Beklemede\";\"{order.WaitingCount}\"");
                        summaryCsv.AppendLine($"\"İletilemedi\";\"{order.UnsuccessfulCount}\"");
                        summaryCsv.AppendLine($"\"İletilemedi\";\"{order.UndeliveredCount}\"");
                        summaryCsv.AppendLine($"\"Zaman Aşımı\";\"{order.ExpiredCount}\"");

                        System.IO.File.WriteAllText(Path.Combine(orderFolderPath, "report-summary.csv"), summaryCsv.ToString());

                        // CSVs by state
                        SaveNumbersCsv(orderFolderPath, "delivered.csv", list.Where(x => x.State == "İletildi").Select(x => x.GSM));
                        SaveNumbersCsv(orderFolderPath, "undelivered.csv", list.Where(x => x.State == "İletilmedi").Select(x => x.GSM));
                        SaveNumbersCsv(orderFolderPath, "waiting.csv", list.Where(x => x.State == "Beklemede" || x.State == "Waiting").Select(x => x.GSM));
                        SaveNumbersCsv(orderFolderPath, "expired.csv", list.Where(x => x.State == "Süre Aşımı" || x.State == "Zaman Aşımı" || x.State == "Expired").Select(x => x.GSM));

                        return Json(new
                        {
                            success = true,
                            message = "Report processed successfully",
                            data = new
                            {
                                processed = order.ProcessedCount,
                                delivered = order.DeliveredCount,
                                undelivered = order.UndeliveredCount,
                                waiting = order.WaitingCount,
                                expired = order.ExpiredCount,
                                refunded = order.RefundAmount,
                                isFinal = isFinal
                            }
                        });
                    }

                    return Json(new { success = false, message = "Failed to parse API response" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing manual report for OrderId={orderId}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private void SaveNumbersCsv(string folderPath, string fileName, IEnumerable<string> numbers)
        {
            var path = Path.Combine(folderPath, fileName);
            System.IO.File.WriteAllLines(path, numbers);
        }

        // Response model
        private class YurticiSmsReportResponse
        {
            public ResponseModel Response { get; set; }

            public class ResponseModel
            {
                public ReportModel Report { get; set; }
            }

            public class ReportModel
            {
                public List<ReportItem> List { get; set; }
            }

            public class ReportItem
            {
                public string GSM { get; set; }
                public string State { get; set; }
            }
        }
    }
}