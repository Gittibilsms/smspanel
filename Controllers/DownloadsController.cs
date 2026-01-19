using ClosedXML.Excel;
using GittBilSmsCore.Data;
using GittBilSmsCore.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Text;

namespace GittBilSmsCore.Controllers
{
    [Route("Downloads")]
    public class DownloadsController : BaseController
    {
        private readonly IStringLocalizer _sharedLocalizer;
        private readonly GittBilSmsDbContext _context;
        private readonly TelegramMessageService _svc;
        private readonly IWebHostEnvironment _env;

        public DownloadsController(
            GittBilSmsDbContext context,
            IStringLocalizerFactory factory,
            IWebHostEnvironment env,
            TelegramMessageService svc)
          : base(context)
        {
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
            _context = context;
            _env = env;
            _svc = svc;
        }

        public IActionResult Index()
        {
            return View();
        }

        #region Export Endpoint

        [HttpGet("Export")]
        public async Task<IActionResult> Export(int orderId, string type, string format)
        {
            // ✅ 1. Authorization check
            var (isAuthorized, order, errorMessage) = await CanAccessOrderAsync(orderId);

            if (order == null)
                return NotFound(errorMessage);

            if (!isAuthorized)
            {
                await LogUnauthorizedAccessAsync(orderId, "Unauthorized download attempt", "indirme");
                return Forbid();
            }

            // ✅ 2. Check if order is older than 1 week
            var orderAge = TimeHelper.NowInTurkey() - order.CreatedAt;
            bool isExpired = orderAge.TotalDays > 7;

            // ✅ 3. Validate format
            var validFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "txt", "csv", "xlsx" };
            if (string.IsNullOrWhiteSpace(format) || !validFormats.Contains(format))
                return BadRequest("Desteklenmeyen dosya formatı.");

            // ✅ 4. Validate type
            var validTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "recipients", "delivered", "failed", "pending", "undelivered","original"
            };
            if (string.IsNullOrWhiteSpace(type) || !validTypes.Contains(type))
                return BadRequest("Geçersiz dosya tipi.");

            // ✅ 5. Build file path
            var home = Environment.GetEnvironmentVariable("HOME") ?? _env.ContentRootPath;
            var ordersRoot = Path.Combine(home, "data", "orders");
            var folderPath = Path.Combine(ordersRoot, orderId.ToString());

            var txtFile = type.ToLower() switch
            {
                "recipients" => Path.Combine(folderPath, "Recipients.txt"),
                "original" => Path.Combine(folderPath, "original.txt"),
                _ => Path.Combine(folderPath, $"{type.ToLower()}.txt")
            };

            if (!System.IO.File.Exists(txtFile))
                return NotFound("Kaynak metin dosyası bulunamadı.");

            var fileName = $"Order_No_{orderId}-{type}.{format}";

            // ✅ 6. Log download
            await LogDownloadAsync(orderId, fileName, type, format, "File downloaded from orders", "Filedownloadmessage");

            // ✅ 7. If order is older than 1 week, return empty file
            if (isExpired)
            {
                return format.ToLower() switch
                {
                    "txt" => File(Array.Empty<byte>(), "text/plain", fileName),
                    "csv" => File(Encoding.UTF8.GetBytes("PhoneNumber"), "text/csv", fileName),
                    "xlsx" => ExportNumbersToExcel(Array.Empty<string>(), fileName),
                    _ => BadRequest("Desteklenmeyen format.")
                };
            }

            // ✅ 8. Read file async
            var numbers = await System.IO.File.ReadAllLinesAsync(txtFile);

            // ✅ 9. Return file based on format
            return format.ToLower() switch
            {
                "txt" => File(Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, numbers)),
                              "text/plain", fileName),
                "csv" => File(Encoding.UTF8.GetBytes("PhoneNumber\n" + string.Join("\n", numbers)),
                              "text/csv", fileName),
                "xlsx" => ExportNumbersToExcel(numbers, fileName),
                _ => BadRequest("Desteklenmeyen format.")
            };
        }

        #endregion

        #region Report Download Endpoint

        [HttpGet("DownloadReportFile")]
        public async Task<IActionResult> DownloadReportFile(int orderId, string fileName, string reportName)
        {
            // ✅ 1. Authorization check
            var (isAuthorized, order, errorMessage) = await CanAccessOrderAsync(orderId);

            if (order == null)
                return NotFound(errorMessage);

            if (!isAuthorized)
            {
                await LogUnauthorizedAccessAsync(orderId, "Unauthorized report download attempt", "rapor indirme", fileName);
                return Forbid();
            }

            // ✅ 2. Check if order is older than 1 week
            var orderAge = TimeHelper.NowInTurkey() - order.CreatedAt;
            bool isExpired = orderAge.TotalDays > 7;

            // ✅ 3. Validate fileName
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest("Dosya adı gerekli.");

            // ✅ 4. Validate extension
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var validFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".csv", ".xlsx" };

            if (!validFormats.Contains(ext))
                return BadRequest("Desteklenmeyen dosya formatı.");

            // ✅ 5. Path traversal protection
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(baseName) ||
                baseName.Contains("..") ||
                baseName.Contains("/") ||
                baseName.Contains("\\") ||
                baseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return BadRequest("Geçersiz dosya adı.");
            }

            // ✅ 6. Find file path
            var home = Environment.GetEnvironmentVariable("HOME") ?? _env.ContentRootPath;
            var ordersRoot = Path.Combine(home, "data", "orders");
            var folder = Path.Combine(ordersRoot, orderId.ToString());

            var csvPath = Path.Combine(folder, baseName + ".csv");
            var txtPath = Path.Combine(folder, baseName + ".txt");

            string source;
            if (System.IO.File.Exists(csvPath))
                source = csvPath;
            else if (System.IO.File.Exists(txtPath))
                source = txtPath;
            else
                return NotFound($"'{baseName}' dosyası bulunamadı.");

            // ✅ 7. Download name
            var downloadName = !string.IsNullOrWhiteSpace(reportName) ? reportName : fileName;

            // ✅ 8. Log download
            await LogDownloadAsync(orderId, fileName, baseName, ext.TrimStart('.'),
                "Report downloaded from orders", "Reportdownloadmessage");

            // ✅ 9. If order is older than 1 week, return empty file
            if (isExpired)
            {
                return ext switch
                {
                    ".csv" => File(Array.Empty<byte>(), "text/csv", downloadName),
                    ".txt" => File(Array.Empty<byte>(), "text/plain", downloadName),
                    ".xlsx" => ExportEmptyExcel(downloadName),
                    _ => BadRequest("Desteklenmeyen format.")
                };
            }

            // ✅ 10. Return file based on format
            return ext switch
            {
                ".csv" => File(await System.IO.File.ReadAllBytesAsync(source), "text/csv", downloadName),
                ".txt" => File(await System.IO.File.ReadAllBytesAsync(source), "text/plain", downloadName),
                ".xlsx" => await ExportReportToExcelAsync(source, downloadName),
                _ => BadRequest("Desteklenmeyen format.")
            };
        }

        #endregion

        #region Private Helper Methods

        private IActionResult ExportNumbersToExcel(string[] numbers, string fileName)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Numbers");

            worksheet.Cell(1, 1).Value = "PhoneNumber";
            for (int i = 0; i < numbers.Length; i++)
            {
                worksheet.Cell(i + 2, 1).Value = numbers[i];
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        private IActionResult ExportEmptyExcel(string downloadName)
        {
            using var wb = new XLWorkbook();
            wb.Worksheets.Add("Report");
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                downloadName);
        }

        private async Task<IActionResult> ExportReportToExcelAsync(string source, string downloadName)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Report");
            var lines = await System.IO.File.ReadAllLinesAsync(source);

            for (int r = 0; r < lines.Length; r++)
            {
                string[] cells = source.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                    ? lines[r].Split(',')
                    : new[] { lines[r] };

                for (int c = 0; c < cells.Length; c++)
                    ws.Cell(r + 1, c + 1).Value = cells[c];
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);

            return File(
                ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                downloadName
            );
        }

        private async Task LogUnauthorizedAccessAsync(int orderId, string message, string actionType, string fileName = null)
        {
            int currentUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
            int? currentCompanyId = HttpContext.Session.GetInt32("CompanyId");

            string dataJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                Message = message,
                AttemptedOrderId = orderId,
                FileName = fileName,
                UserId = currentUserId,
                CompanyId = currentCompanyId,
                Time = TimeHelper.NowInTurkey(),
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString()
            });

            try
            {
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _svc.SendToUsersAsync(
                    currentCompanyId ?? 0,
                    currentUserId,
                    $"⚠️ Yetkisiz {actionType} denemesi: Order #{orderId}",
                    dataJson,
                    "",
                    2,
                    cts.Token
                );
            }
            catch
            {
                // Don't let logging failure break the request
            }
        }

        private async Task LogDownloadAsync(int orderId, string fileName, string type, string format,
            string logMessage, string localizerKey)
        {
            int performedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
            int? companyId = HttpContext.Session.GetInt32("CompanyId") ?? 0;
            var userName = _context.Users.Find(performedByUserId)?.UserName ?? "UnknownUser";

            var filerewriteName = $"{type}.{format}";
            var textMsg = string.Format(
                _sharedLocalizer[localizerKey],
                filerewriteName,
                orderId,
                userName,
                TimeHelper.NowInTurkey()
            );

            string dataJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                Message = logMessage,
                UserName = userName,
                OrderId = orderId,
                FileName = fileName,
                Time = TimeHelper.NowInTurkey(),
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString()
            });

            try
            {
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _svc.SendToUsersAsync(companyId.Value, performedByUserId, textMsg, dataJson, "", 1, cts.Token);
            }
            catch
            {
                // Don't let logging failure break the download
            }
        }

        #endregion
    }
}