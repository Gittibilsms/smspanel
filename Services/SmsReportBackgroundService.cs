using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GittBilSmsCore.Data;
using Microsoft.EntityFrameworkCore;
using System.IO;
using GittBilSmsCore.Models;
using GittBilSmsCore.Helpers;
public class SmsReportBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SmsReportBackgroundService> _logger;
    private readonly IWebHostEnvironment _env;

    public SmsReportBackgroundService(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory, ILogger<SmsReportBackgroundService> logger, IWebHostEnvironment env)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _env = env;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingReports();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SmsReportBackgroundService");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken); // Run every 6 hours
        }
    }

    public async Task RunManualReport()
    {
        _logger.LogInformation("Manual report trigger started 🚀");
        await ProcessPendingReports();
        _logger.LogInformation("Manual report trigger completed ✅");
    }

    private async Task ProcessPendingReports()
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<GittBilSmsDbContext>();

            var supportedApis = new[] { "yurtici", "telsim", "turkcell" };

            var orders = await dbContext.Orders
                .Include(o => o.Company)
                .Include(o => o.Api)
                .Where(o => o.ReportLock
                            && o.CompletedAt != null
                            && o.SmsOrderId != null
                            && o.CurrentStatus == "Sent"
                            && supportedApis.Contains(o.Api.ServiceName.ToLower()))
                .ToListAsync();

            _logger.LogInformation($"Found {orders.Count} orders pending report.");

            foreach (var order in orders)
            {
                try
                {
                    var httpClient = _httpClientFactory.CreateClient();

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

                        _logger.LogInformation($"Calling API [{order.Api.ServiceName}] (JSON) for OrderId={order.OrderId}, SmsOrderId={order.SmsOrderId}");
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

                        _logger.LogInformation($"Calling API [{order.Api.ServiceName}] (XML) for OrderId={order.OrderId}, SmsOrderId={order.SmsOrderId}");
                    }
                    else
                    {
                        _logger.LogWarning($"Unsupported ContentType [{contentType}] for ApiId={order.ApiId}. Skipping OrderId={order.OrderId}");
                        continue;
                    }

                    // Call API
                    var response = await httpClient.PostAsync(apiUrl, content);
                    response.EnsureSuccessStatusCode();

                    var responseBody = await response.Content.ReadAsStringAsync();

                    _logger.LogInformation($"Response for OrderId={order.OrderId}: {responseBody}");

                    // Parse response
                    var smsReportResponse = JsonSerializer.Deserialize<YurticiSmsReportResponse>(responseBody);

                    if (smsReportResponse?.Response?.Report?.List != null)
                    {
                        var list = smsReportResponse.Response.Report.List;

                        // Save counts
                        order.ProcessedCount = list.Count;
                        //order.DeliveredCount = list.Count(x => x.State == "Teslim Edildi");
                        order.DeliveredCount = list.Count(x => x.State == "İletildi");
                        order.UndeliveredCount = list.Count(x => x.State == "İletilmedi");
                        order.UnsuccessfulCount = list.Count(x => x.State == "İletilmedi");
                        order.WaitingCount = list.Count(x => x.State == "Beklemede" || x.State == "Waiting");
                        order.ExpiredCount = list.Count(x => x.State == "Süre Aşımı" || x.State == "Zaman Aşımı" || x.State == "Expired");

                        // 48h check
                        bool isFinal = order.StartedAt.HasValue && (DateTime.UtcNow.AddHours(3) - order.StartedAt.Value).TotalHours >= 48;

                        if (isFinal)
                        {
                            order.ReportLock = false;
                            order.ReportedAt = DateTime.UtcNow.AddHours(3);
                            _logger.LogInformation($"48h passed → FINAL report for OrderId={order.OrderId}");

                            // 🚀 REFUND LOGIC → SAME AS CADDESMS:

                            if (order.Refundable && order.UnsuccessfulCount > 0 && !order.Returned)
                            {
                                // 🚩 1️⃣ Determine unit price based on LoadedCount
                                decimal unitPrice = 0;

                                if (order.LoadedCount <= 500_000)
                                {
                                    unitPrice = order.Company.LowPrice.GetValueOrDefault();
                                }
                                else if (order.LoadedCount <= 1_000_000)
                                {
                                    unitPrice = order.Company.MediumPrice.GetValueOrDefault();
                                }
                                else
                                {
                                    unitPrice = order.Company.HighPrice.GetValueOrDefault();
                                }

                                // 🚩 2️⃣ Calculate refund amount
                                var refundAmount = order.UnsuccessfulCount * unitPrice;

                                order.RefundAmount = refundAmount;

                                // 🚩 3️⃣ Refund to company credit
                                order.Company.CreditLimit += refundAmount;

                                // 🚩 4️⃣ Mark order as returned
                                order.Returned = true;
                                order.ReturnDate = DateTime.UtcNow.AddHours(3);

                                _logger.LogInformation($"Refunded {refundAmount} to CompanyId={order.CompanyId} for OrderId={order.OrderId} (UnsuccessfulCount={order.UnsuccessfulCount}, UnitPrice={unitPrice})");
                            }
                        }
                        else
                        {
                            _logger.LogInformation($"Interim report saved for OrderId={order.OrderId}, will continue checking until 48h.");
                        }

                        await dbContext.SaveChangesAsync();

                        // Save CSVs
                        var orderFolderPath = Path.Combine("D:\\home\\data", "orders", order.OrderId.ToString());
                        System.IO.Directory.CreateDirectory(orderFolderPath);

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

                        _logger.LogInformation($"CSV files saved for OrderId={order.OrderId}");
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to parse report for OrderId={order.OrderId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing report for OrderId={order.OrderId}");
                }
            }
        }
    }

    private void SaveNumbersCsv(string folderPath, string fileName, IEnumerable<string> numbers)
    {
        var path = Path.Combine(folderPath, fileName);
        System.IO.File.WriteAllLines(path, numbers);
    }

    // Yurtici API → full response model
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