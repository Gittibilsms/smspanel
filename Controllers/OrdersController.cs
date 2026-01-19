using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GittBilSmsCore.Data;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Threading.Tasks;
using GittBilSmsCore.ViewModels;
using GittBilSmsCore.Helpers;
using GittBilSmsCore.Models;
using Newtonsoft.Json;
using System.Text;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Mvc.Rendering;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.ExtendedProperties;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using GittBilSmsCore.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GittBilSmsCore.Controllers
{
    public class OrdersController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly IStringLocalizer _sharedLocalizer;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<ChatHub> _hubContext;

        public OrdersController(GittBilSmsDbContext context, IHubContext<ChatHub> hubContext, IStringLocalizerFactory factory, IWebHostEnvironment env) : base(context)
        {
            _context = context;
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
            _env = env;
            _hubContext = hubContext;
        }

        public IActionResult Index()
        {
            if (!HasAccessRoles("Order", "Read"))
            {
                return Forbid();
            }
            ViewBag.CompanyId = HttpContext.Session.GetInt32("CompanyId") ?? 0;
            ViewBag.IsAdmin = HttpContext.Session.GetString("UserType") == "Admin"
                              || HttpContext.Session.GetInt32("RoleId") == 1;
            ViewBag.UserId = HttpContext.Session.GetInt32("UserId") ?? 0;
            ViewBag.IsMainUser = HttpContext.Session.GetInt32("IsMainUser") == 1;

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetAllOrders(string status = null)
        {
            var userType = HttpContext.Session.GetString("UserType") ?? "";
            var companyId = HttpContext.Session.GetInt32("CompanyId");
            var userId = HttpContext.Session.GetInt32("UserId");

            var isMainUser = HttpContext.Session.GetInt32("IsMainUser") == 1;

            var ordersQuery = _context.Orders
                .Include(o => o.Company)
                .Include(o => o.Api)
                .Include(o => o.CreatedByUser)
                .AsQueryable();

            if (userType == "CompanyUser" && companyId.HasValue)
            {
                if (isMainUser)
                {
                    ordersQuery = ordersQuery.Where(o => o.CompanyId == companyId.Value);
                }
                else
                {
                    ordersQuery = ordersQuery.Where(o => o.CompanyId == companyId.Value && o.CreatedByUserId == userId);
                }
            }

            if (!string.IsNullOrEmpty(status))
            {
                var normalizedStatus = status.Trim();
                ordersQuery = ordersQuery.Where(o =>
                    (o.CurrentStatus == "AwaitingApproval" || o.CurrentStatus == "Awaiting approval") && normalizedStatus == "AwaitingApproval" ||
                    (o.CurrentStatus == "Waiting to be sent" || o.CurrentStatus == "WaitingToBeSent") && normalizedStatus == "Waiting to be sent" ||
                    (o.CurrentStatus == "Shipping has started") && normalizedStatus == "Shipping has started" ||
                    (o.CurrentStatus == "Sent") && normalizedStatus == "Sent" ||
                    (o.CurrentStatus == "Failed" || o.CurrentStatus == "Sending failed" || o.CurrentStatus == "Submission failed") && normalizedStatus == "Failed" ||
                    (o.CurrentStatus == "Cancelled") && normalizedStatus == "Cancelled" ||
                    o.CurrentStatus == normalizedStatus
                );
            }

            var orders = await ordersQuery
                .OrderByDescending(o => o.OrderId)
               .Select(o => new
               {
                   o.OrderId,
                   o.CompanyId, // ✅ Add this line
                   Status = o.CurrentStatus,
                   CompanyName = o.Company != null ? o.Company.CompanyName : null,
                   ApiName = o.Api != null ? o.Api.ServiceName : null,
                   o.SubmissionType,
                   o.LoadedCount,
                   o.DeliveredCount,
                   o.UnsuccessfulCount,
                   CreatedBy = o.CreatedByUser != null ? o.CreatedByUser.FullName : null,
                   DateOfSending = o.ScheduledSendDate,
                   o.Refundable,
                   o.Returned,
                   o.ReturnDate,
                   o.CreatedAt,
                   o.WaitingCount,
                   o.ExpiredCount,
                   o.RefundAmount,
                   o.UndeliveredCount,
                   o.InvalidCount,  
                   o.BlacklistedCount,  
                   o.RepeatedCount, 
                   o.BannedCount,
                   IsInsufficientBalanceFailure = o.ApiErrorResponse != null &&
            (o.ApiErrorResponse.Contains("insufficientbal") ||
             o.ApiErrorResponse.Contains("Insufficient balance") ||
             o.ApiErrorResponse.Contains("Yetersiz bakiye"))
               })//.Where(o => o.OrderId == 49215) // Dummy where to ensure IQueryable 
                .ToListAsync();

            return Json(orders);
        }
        [HttpGet]
        public async Task<IActionResult> GetApis()
        {
            var apis = await _context.Apis
                .Where(a => a.IsActive && !a.IsClientApi)
                .Select(a => new { a.ApiId, a.ServiceName })
                .ToListAsync();

            return Json(apis);
        }
        // Download Report Summary
        [HttpGet]
        public async Task<IActionResult> DownloadReportSummary(int orderId)
        {
            return await DownloadReportFile(orderId, "report-summary.csv", "Summary");
        }
        // Download Undelivered
        [HttpGet]
        public async Task<IActionResult> DownloadUndelivered(int orderId)
        {
            return await DownloadReportFile(orderId, "undelivered.csv", "Undelivered");
        }

        // Download Forwarded
        [HttpGet]
        public async Task<IActionResult> DownloadWaiting(int orderId)
        {
            return await DownloadReportFile(orderId, "waiting.csv", "Waiting");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadAllReport(int orderId)
        {
            return await DownloadReportFile(orderId, "all.csv", "All");
        }
        // Download Waiting
        [HttpGet]
        public async Task<IActionResult> DownloadForwarded(int orderId)
        {
            return await DownloadReportFile(orderId, "delivered.csv", "Forwarded");
        }

        // Download Expired
        [HttpGet]
        public async Task<IActionResult> DownloadExpired(int orderId)
        {
            return await DownloadReportFile(orderId, "expired.csv", "Expired");
        }
        private string BuildRequestBody(SendSmsViewModel model, Api api)
        {
            var rawNumbers = model.PhoneNumbers ?? "";
            var numbersList = rawNumbers
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            switch (api.ServiceName?.ToLower())
            {
                case "yurtici":
                    // Format 1
                    return JsonConvert.SerializeObject(new
                    {
                        Username = api.Username,
                        Password = api.Password,
                        // From = api.Originator,
                        Text = model.Message,
                        To = numbersList
                    });

                case "turkcell":
                    // Format 2
                    return JsonConvert.SerializeObject(new
                    {
                        User = api.Username,
                        Pass = api.Password,
                        Message = model.Message,
                        Numbers = numbersList
                    });

                default:
                    // Fallback based on content-type
                    if (api.ContentType == "application/json")
                    {
                        return JsonConvert.SerializeObject(new
                        {
                            Username = api.Username,
                            Password = api.Password,
                            From = api.Originator,
                            Text = model.Message,
                            To = numbersList
                        });
                    }
                    else if (api.ContentType == "text/xml")
                    {
                        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
                <SendSms>
                  <Username>{api.Username}</Username>
                  <Password>{api.Password}</Password>
                  <From>{api.Originator}</From>
                  <Text>{model.Message}</Text>
                  <To>{string.Join(",", numbersList)}</To>
                </SendSms>";
                    }

                    throw new InvalidOperationException("Unsupported ContentType or API format");
            }
        }
        [HttpGet]
        public async Task<IActionResult> DownloadRecipients(int orderId)
        {
            var order = await _context.Orders.FindAsync(orderId);

            if (order == null || string.IsNullOrEmpty(order.FilePath))
                return NotFound("Order not found or file path is missing.");

            var filePath = Path.Combine(_env.WebRootPath, order.FilePath.TrimStart('/'));

            if (!System.IO.File.Exists(filePath))
                return NotFound("File not found on server.");

            var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            var downloadName = $"Order_{orderId}_recipients.txt";

            return File(fileBytes, "text/plain", downloadName);
        }
        public IActionResult ListReportFiles(int orderId)
        {
            var reportsRoot = ReportPathHelper.GetReportsRootPath(_env);

            var orderFolderPath = Path.Combine(reportsRoot, orderId.ToString());

            if (!System.IO.Directory.Exists(orderFolderPath))
            {
                return NotFound($"No reports found for order {orderId}.");
            }

            var files = System.IO.Directory.GetFiles(orderFolderPath)
                                 .Select(Path.GetFileName)
                                 .ToList();

            ViewBag.OrderId = orderId;
            ViewBag.Files = files;

            return View();
        }
        [HttpGet]
        public async Task<IActionResult> DownloadReportFile(int orderId, string fileName, string reportName)
        {
            // ✅ 1. Authorization check
            var (isAuthorized, order, errorMessage) = await CanAccessOrderAsync(orderId);

            if (order == null)
                return NotFound(errorMessage);

            if (!isAuthorized)
                return Forbid();

            // ✅ 2. Check if order is older than 1 week
            var orderAge = TimeHelper.NowInTurkey() - order.CreatedAt;
            bool isExpired = orderAge.TotalDays > 7;

            // 3️⃣ Find the Kudu/App_Data path
            var home = Environment.GetEnvironmentVariable("HOME")
                             ?? _env.ContentRootPath;
            var ordersRoot = Path.Combine(home, "data", "orders");
            var folder = Path.Combine(ordersRoot, orderId.ToString());

            // 4️⃣ Build both possible source paths (CSV or TXT)
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var csvPath = Path.Combine(folder, baseName + ".csv");
            var txtPath = Path.Combine(folder, baseName + ".txt");

            // 5️⃣ Pick whichever exists
            string source;
            if (System.IO.File.Exists(csvPath))
                source = csvPath;
            else if (System.IO.File.Exists(txtPath))
                source = txtPath;
            else
                return NotFound($"Neither '{baseName}.csv' nor '{baseName}.txt' was found for order {orderId}.");

            // 6️⃣ Final download‑as name
            var downloadName = reportName ?? fileName;

            // 7️⃣ Branch on requested extension
            var ext = Path.GetExtension(fileName).ToLowerInvariant();

            // ✅ 8. If order is older than 1 week, return empty file
            if (isExpired)
            {
                switch (ext)
                {
                    case ".csv":
                        return File(Array.Empty<byte>(), "text/csv", downloadName);
                    case ".txt":
                        return File(Array.Empty<byte>(), "text/plain", downloadName);
                    case ".xlsx":
                        using (var wb = new XLWorkbook())
                        {
                            wb.Worksheets.Add("Report");
                            using var ms = new MemoryStream();
                            wb.SaveAs(ms);
                            return File(ms.ToArray(),
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                downloadName);
                        }
                    default:
                        return BadRequest("Unsupported format");
                }
            }

            // 9️⃣ Return file with data
            switch (ext)
            {
                case ".csv":
                    {
                        var data = await System.IO.File.ReadAllBytesAsync(source);
                        return File(data, "text/csv", downloadName);
                    }
                case ".txt":
                    {
                        var data = await System.IO.File.ReadAllBytesAsync(source);
                        return File(data, "text/plain", downloadName);
                    }
                case ".xlsx":
                    {
                        // Build an in‑memory XLSX from CSV/TXT
                        using var wb = new XLWorkbook();
                        var ws = wb.Worksheets.Add("Report");
                        var lines = await System.IO.File.ReadAllLinesAsync(source);

                        for (int r = 0; r < lines.Length; r++)
                        {
                            string[] cells;
                            if (source.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                                cells = lines[r].Split(',');
                            else
                                cells = new[] { lines[r] }; // TXT: whole line in col A

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
                default:
                    return BadRequest("Unsupported format");
            }
        }

        [HttpPost]
        public async Task<IActionResult> ChangeApiAndResend(int orderId, int apiId)
        {
            var order = await _context.Orders
                .Include(o => o.Company)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return NotFound("Order not found.");

            var api = await _context.Apis.FirstOrDefaultAsync(a => a.ApiId == apiId && a.IsActive);
            if (api == null)
                return BadRequest("Invalid API selected.");

            var company = order.Company;

            var isAzure = Environment.GetEnvironmentVariable("HOME") != null;
            var baseFolderPath = isAzure
                ? Path.Combine("D:\\home\\data", "orders")
                : Path.Combine(System.IO.Directory.GetCurrentDirectory(), "App_Data", "orders");

            var folderPath = Path.Combine(baseFolderPath, orderId.ToString());
            var originalFile = Path.Combine(folderPath, "original.txt");

            if (!System.IO.File.Exists(originalFile))
                return BadRequest("Original number list not found.");

            var numbersList = (await System.IO.File.ReadAllLinesAsync(originalFile)).ToList();

            // Reuse blacklist + banned logic
            var blacklist = _context.BlacklistNumbers.Select(x => x.Number).ToHashSet();
            var banned = _context.BannedNumbers.Select(x => x.Number).ToHashSet();

            var validNumbers = new List<string>();
            var seenNumbers = new HashSet<string>();

            bool IsValidPhone(string num)
            {
                num = num.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Trim();
                if (num.StartsWith("+90")) num = num.Substring(1);
                if (num.StartsWith("90") && num.Length == 12) return true;
                if (num.StartsWith("05") && num.Length == 11) return true;
                if (num.StartsWith("5") && num.Length == 10) return true;
                return false;
            }

            foreach (var number in numbersList)
            {
                var cleaned = number.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Trim();
                if (!IsValidPhone(cleaned) || blacklist.Contains(cleaned) || banned.Contains(cleaned) || seenNumbers.Contains(cleaned))
                    continue;

                validNumbers.Add(cleaned);
                seenNumbers.Add(cleaned);
            }

            if (!validNumbers.Any())
                return BadRequest("No valid recipients found for this order.");

            // ✅ Check balance
            var globalPricing = await _context.Pricing.FirstOrDefaultAsync();
            if (globalPricing == null)
                return BadRequest("Global pricing configuration is missing.");

            decimal low = company.LowPrice ?? globalPricing.Low;
            decimal medium = company.MediumPrice ?? globalPricing.Middle;
            decimal high = company.HighPrice ?? globalPricing.High;

            decimal pricePerSms = validNumbers.Count <= 500_000 ? low :
                                  validNumbers.Count <= 1_000_000 ? medium : high;

            var totalCost = validNumbers.Count * pricePerSms;
            var availableBalance = company.CreditLimit;

            if (availableBalance < totalCost)
                return BadRequest("Insufficient balance for resending.");

            // 💰 Deduct balance
            company.CreditLimit -= totalCost;

            order.ApiId = apiId;
            order.TotalPrice = totalCost;
            order.PricePerSms = pricePerSms;
            order.CurrentStatus = "WaitingToBeSent";

            _context.BalanceHistory.Add(new BalanceHistory
            {
                CompanyId = company.CompanyId,
                Amount = -totalCost,
                Action = "Deduct on Resend",
                CreatedAt = TimeHelper.NowInTurkey(),
                CreatedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0
            });

            // 🚀 Resend via new API
            var requestBody = BuildRequestBody(new SendSmsViewModel
            {
                SelectedApiId = apiId,
                CompanyId = company.CompanyId,
                Message = order.MessageText,
                PhoneNumbers = string.Join(",", validNumbers)
            }, api);

            using var httpClient = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, api.ApiUrl)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, api.ContentType)
            };

            var response = await httpClient.SendAsync(request);
            var result = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                order.CurrentStatus = "Failed";
                order.ApiErrorResponse = result;

                _context.BalanceHistory.Add(new BalanceHistory
                {
                    CompanyId = company.CompanyId,
                    Amount = totalCost,
                    Action = "Refund on Failed Resend",
                    CreatedAt = TimeHelper.NowInTurkey(),
                    CreatedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0
                });

                company.CreditLimit += totalCost;

                await _context.SaveChangesAsync();
                return StatusCode((int)response.StatusCode, "Resend failed.");
            }

            dynamic json = JsonConvert.DeserializeObject(result);

            if (json.Status == "OK")
            {
                order.CurrentStatus = "Sent";
                order.SmsOrderId = json.MessageId;
                order.CompletedAt = TimeHelper.NowInTurkey();
                order.Actions.Add(new OrderAction
                {
                    ActionName = "Sent (via API Change)",
                    Message = $"Re-sent using API {api.ServiceName}",
                    CreatedAt = TimeHelper.NowInTurkey()
                });

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = _sharedLocalizer["smsresentsuccess"],
                    messageId = json.MessageId
                });
            }

            return BadRequest($"SMS resend failed: {json.Status}");
        }
        [HttpPost]
        public async Task<IActionResult> ApproveOrder(int orderId)
        {
            var order = await _context.Orders
                .Include(o => o.Company)
                .Include(o => o.Api)
                 .Include(o => o.CreatedByUser)
                .Include(o => o.Actions.OrderBy(a => a.CreatedAt)) // <== ADD THIS!
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null || order.CurrentStatus != "AwaitingApproval")
                return Json(new { success = false, message = _sharedLocalizer["ordernotfoundmsg"] });

            try
            {
                // Build request body from stored order
                var isAzure = Environment.GetEnvironmentVariable("HOME") != null;

                var baseFolderPath = isAzure
                    ? Path.Combine("D:\\home\\data", "orders")
                    : Path.Combine(System.IO.Directory.GetCurrentDirectory(), "App_Data", "orders");
                string fullFilePath;
                if (!string.IsNullOrWhiteSpace(order.FilePath))
                {
                    fullFilePath = isAzure
                        ? Path.Combine("D:\\home", order.FilePath.Replace("/", "\\"))
                        : Path.Combine(System.IO.Directory.GetCurrentDirectory(), "App_Data", order.FilePath.Replace("/", "\\"));
                }
                else
                {
                    // Build it manually from order ID if FilePath not stored
                    fullFilePath = Path.Combine(baseFolderPath, order.OrderId.ToString(), "recipients.txt");
                }
                List<string> numbers = new();

                if (System.IO.File.Exists(fullFilePath))
                {

                    numbers = (await System.IO.File.ReadAllLinesAsync(fullFilePath))
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => n.Trim())
                        .ToList();
                    if (numbers.Count == 0)
                    {
                        order.CurrentStatus = "Failed";
                        order.ApiErrorResponse = "No valid recipients. Cannot send SMS.";

                        order.Actions.Add(new OrderAction
                        {
                            ActionName = "Sending failed",
                            Message = _sharedLocalizer["novalidreceipents"],
                            CreatedAt = TimeHelper.NowInTurkey()
                        });

                        await _context.SaveChangesAsync();

                        return BadRequest("No valid recipients. Cannot send SMS.");
                    }
                }
                else
                {
                    return BadRequest("Recipient file not found.");
                }
                var api = order.Api;
                var company = order.Company;
                var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
                var globalPricing = await _context.Pricing.FirstOrDefaultAsync();
                if (globalPricing == null)
                    return BadRequest("Global pricing configuration is missing.");

                decimal low = company.LowPrice ?? globalPricing.Low;
                decimal medium = company.MediumPrice ?? globalPricing.Middle;
                decimal high = company.HighPrice ?? globalPricing.High;

                decimal pricePerSms = numbers.Count <= 500_000 ? low : (numbers.Count <= 1_000_000 ? medium : high);
                var totalCost = numbers.Count * pricePerSms;

                if (company.CreditLimit < totalCost)
                {
                    order.CurrentStatus = "Failed";
                    order.ApiErrorResponse = "Insufficient balance.";
                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Sending failed",
                        Message = _sharedLocalizer["insufficientbal"],
                        CreatedAt = TimeHelper.NowInTurkey()
                    });
                    await _context.SaveChangesAsync();
                    return BadRequest("Insufficient balance.");
                }

                company.CreditLimit -= totalCost;
                order.PricePerSms = pricePerSms;
                order.TotalPrice = totalCost;

                _context.BalanceHistory.Add(new BalanceHistory
                {
                    CompanyId = company.CompanyId,
                    Amount = -totalCost,
                    Action = "Deduct on Send (Approval)",
                    CreatedAt = TimeHelper.NowInTurkey(),
                    CreatedByUserId = userId
                });


                _context.Orders.Update(order);
                _context.Companies.Update(company);
                await _context.SaveChangesAsync();

                string requestBody;
                if (api.ServiceName.ToLower() == "turkcell")
                {
                    requestBody = JsonConvert.SerializeObject(new
                    {
                        User = api.Username,
                        Pass = api.Password,
                        Message = order.MessageText,
                        Numbers = numbers
                    });
                }
                else if (api.ContentType == "application/json")
                {
                    requestBody = JsonConvert.SerializeObject(new
                    {
                        Username = api.Username,
                        Password = api.Password,
                        Text = order.MessageText,
                        To = numbers
                    });
                }
                else if (api.ContentType == "application/json")
                {
                    requestBody = JsonConvert.SerializeObject(new
                    {
                        Username = api.Username,
                        Password = api.Password,
                        Text = order.MessageText,
                        To = numbers
                    });
                }
                else
                {
                    return BadRequest("Unsupported API format.");
                }

                using var client = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Post, api.ApiUrl)
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, api.ContentType)
                };

                var response = await client.SendAsync(request);
                var result = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // 🚀 Save original response
                    order.ApiErrorResponse = $"HTTP {(int)response.StatusCode} - {result}";
                    order.CurrentStatus = "Failed";

                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Sending failed",
                        Message = order.ApiErrorResponse,
                        CreatedAt = TimeHelper.NowInTurkey()
                    });

                    //// 🚀 Refund if balance was deducted
                    //if (order.TotalPrice > 0 && order.Returned == false)
                    //{
                    //    order.Company.CurrentBalance += order.TotalPrice.Value;

                    //    order.Refundable = true;
                    //    order.Returned = true;
                    //    order.ReturnDate = DateTime.UtcNow.AddHours(3);

                    //    _context.BalanceHistory.Add(new BalanceHistory
                    //    {
                    //        CompanyId = order.CompanyId,
                    //        Amount = order.TotalPrice.Value,
                    //        Action = "Refund on Failed",
                    //        CreatedAt = DateTime.UtcNow.AddHours(3),
                    //        CreatedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0
                    //    });
                    //}

                    _context.Orders.Update(order);
                    await _context.SaveChangesAsync();
                    return StatusCode((int)response.StatusCode, $"SMS API failed: {result}");
                }
                dynamic json = JsonConvert.DeserializeObject(result);

                if (json.Status == "OK")
                {
                    order.SmsOrderId = json.MessageId;
                    order.StartedAt = TimeHelper.NowInTurkey();
                    order.ReportLock = true;
                    order.CompletedAt = TimeHelper.NowInTurkey();
                    order.ScheduledSendDate = TimeHelper.NowInTurkey();
                    order.CurrentStatus = "Sent";
                    order.Actions.Add(new OrderAction { ActionName = "Shipping has started", CreatedAt = TimeHelper.NowInTurkey() });
                    order.Actions.Add(new OrderAction { ActionName = "Sent", Message = $"MessageId: {json.MessageId}" });
                    _context.Orders.Update(order);
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = _sharedLocalizer["orderapproved"] });
                }
                else
                {
                    // 🚀 Save original response for Admin view
                    // 🚀 Save original response for Admin view
                    order.ApiErrorResponse = $"Status: {json.Status}, Full Response: {result}";
                    order.CurrentStatus = "Failed";

                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Sending failed",
                        Message = order.ApiErrorResponse,
                        CreatedAt = TimeHelper.NowInTurkey()
                    });

                    //// 🚀 Refund if balance was deducted
                    //if (order.TotalPrice > 0 && order.Returned == false)
                    //{
                    //    order.Company.CurrentBalance += order.TotalPrice.Value;

                    //    order.Refundable = true;
                    //    order.Returned = true;
                    //    order.ReturnDate = DateTime.UtcNow.AddHours(3);

                    //    _context.BalanceHistory.Add(new BalanceHistory
                    //    {
                    //        CompanyId = order.CompanyId,
                    //        Amount = order.TotalPrice.Value,
                    //        Action = "Refund on Failed",
                    //        CreatedAt = DateTime.UtcNow.AddHours(3),
                    //        CreatedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0
                    //    });
                    //}

                    await _context.SaveChangesAsync();

                    return BadRequest($"SMS API returned an error: {json.Status}");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Exception occurred: {ex.Message}");
            }
        }
        public IActionResult SendSms()
        {
            var apis = _context.Apis.Where(a => a.IsActive && !a.IsClientApi).ToList();
            ViewBag.ApiList = new SelectList(apis, "ApiId", "ServiceName");
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> SendSms(SendSmsViewModel model)
        {
            try
            {
                Console.WriteLine($"SelectedApiId (Before Default): {model.SelectedApiId}");

                // Fallback to Yurtici API if not selected
                if (model.SelectedApiId == 0)
                {
                    model.SelectedApiId = await _context.Apis
                        .Where(a => a.ServiceName == "Yurtici")
                        .Select(a => a.ApiId)
                        .FirstOrDefaultAsync();

                    if (model.SelectedApiId == 0)
                        return BadRequest("Hiçbir API seçilmedi.");
                }

                var api = await _context.Apis.FirstOrDefaultAsync(a => a.ApiId == model.SelectedApiId);
                if (api == null)
                    return BadRequest("Geçersiz API seçildi.");

                var company = await _context.Companies.FirstOrDefaultAsync(c => c.CompanyId == model.CompanyId);
                if (company == null)
                    return BadRequest("Geçersiz şirket.");

                var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                    return BadRequest("Geçersiz kullanıcı.");


                var rawNumbers = model.PhoneNumbers ?? "";
                var numbersList = rawNumbers
                    .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => n.Trim())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();

                // Create order
                var order = new Order
                {
                    CompanyId = model.CompanyId,
                    DirectoryId = model.DirectoryId,
                    PastMessageId = model.PastMessageId,
                    ApiId = model.SelectedApiId,
                    SubmissionType = "Manual",
                    ScheduledSendDate = model.ScheduledSendDate ?? TimeHelper.NowInTurkey(),
                    MessageText = model.Message,
                    LoadedCount = numbersList.Count,
                    ProcessedCount = 0,
                    UnsuccessfulCount = 0,
                    Refundable = false,
                    Returned = false,
                    CreatedByUserId = userId,
                    CreatedAt = TimeHelper.NowInTurkey()
                };


                if (!company.IsTrustedSender)
                {
                    order.CurrentStatus = "AwaitingApproval";
                    order.SubmissionType = "Manual";

                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Awaiting approval",
                        CreatedAt = TimeHelper.NowInTurkey()
                    });
                }
                else if (model.ScheduledSendDate.HasValue && model.ScheduledSendDate.Value > TimeHelper.NowInTurkey())
                {
                    order.CurrentStatus = "Scheduled";
                    order.SubmissionType = "Scheduled";

                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Scheduled",
                        Message = _sharedLocalizer["scheduledfor", model.ScheduledSendDate.Value.ToString("yyyy-MM-dd HH:mm")],
                        CreatedAt = TimeHelper.NowInTurkey()
                    });
                }
                else
                {
                    order.CurrentStatus = "WaitingToBeSent";
                    order.SubmissionType = "Manual";

                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Queued for sending",
                        CreatedAt = TimeHelper.NowInTurkey()
                    });
                }

                _context.Orders.Add(order);
                await _context.SaveChangesAsync();


                // Blacklist + banned check
                var blacklist = _context.BlacklistNumbers.Select(x => x.Number).ToHashSet();
                var banned = _context.BannedNumbers.Select(x => x.Number).ToHashSet();

                var validNumbers = new List<string>();
                var invalidNumbers = new List<string>();
                var blacklistedNumbers = new List<string>();
                var repeatedNumbers = new List<string>();
                var bannedNumbers = new List<string>();
                var seenNumbers = new HashSet<string>();

                bool IsValidPhone(string num)
                {
                    num = num.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Trim();
                    if (num.StartsWith("+90")) num = num.Substring(1);
                    if (num.StartsWith("90") && num.Length == 12) return true;
                    if (num.StartsWith("05") && num.Length == 11) return true;
                    if (num.StartsWith("5") && num.Length == 10) return true;
                    return false;
                }

                foreach (var number in numbersList)
                {
                    var cleanedNumber = number.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Trim();

                    if (!IsValidPhone(cleanedNumber))
                    {
                        invalidNumbers.Add(cleanedNumber);
                        continue;
                    }

                    if (blacklist.Contains(cleanedNumber))
                    {
                        blacklistedNumbers.Add(cleanedNumber);
                        continue;
                    }

                    if (banned.Contains(cleanedNumber))
                    {
                        bannedNumbers.Add(cleanedNumber);
                        continue;
                    }

                    if (seenNumbers.Contains(cleanedNumber))
                    {
                        repeatedNumbers.Add(cleanedNumber);
                        continue;
                    }

                    validNumbers.Add(cleanedNumber);
                    seenNumbers.Add(cleanedNumber);
                }
                bool isMainUser = user.IsMainUser ?? false;

                if (!isMainUser && user.QuotaType == "Variable Quota")
                {
                    int allowedQuota = user.Quota ?? 0;

                    if (allowedQuota <= 0 || validNumbers.Count > allowedQuota)
                    {
                        order.CurrentStatus = "Failed";
                        order.ApiErrorResponse = _sharedLocalizer["Noquota"];
                        order.Actions.Add(new OrderAction
                        {
                            ActionName = "Sending failed",
                            Message = _sharedLocalizer["quotanotavailable"],
                            CreatedAt = TimeHelper.NowInTurkey()
                        });

                        _context.Orders.Update(order);
                        await _context.SaveChangesAsync();

                        return BadRequest(_sharedLocalizer["quotanotavailable"]);
                    }

                    // ✅ Deduct later only if actually sent/scheduled
                    user.Quota = allowedQuota - validNumbers.Count;
                }
                // Save files
                var isAzure = Environment.GetEnvironmentVariable("HOME") != null;

                // Set base path depending on environment
                var baseFolderPath = isAzure
                    ? Path.Combine("D:\\home\\data", "orders")
                    : Path.Combine(System.IO.Directory.GetCurrentDirectory(), "App_Data", "orders");
                var folderPath = Path.Combine(baseFolderPath, order.OrderId.ToString());
                System.IO.Directory.CreateDirectory(folderPath);

                await System.IO.File.WriteAllLinesAsync(Path.Combine(folderPath, "original.txt"), numbersList);
                await System.IO.File.WriteAllLinesAsync(Path.Combine(folderPath, "recipients.txt"), validNumbers);
                await System.IO.File.WriteAllLinesAsync(Path.Combine(folderPath, "filtered.txt"), validNumbers);
                await System.IO.File.WriteAllLinesAsync(Path.Combine(folderPath, "invalid.txt"), invalidNumbers);
                await System.IO.File.WriteAllLinesAsync(Path.Combine(folderPath, "blacklisted.txt"), blacklistedNumbers);
                await System.IO.File.WriteAllLinesAsync(Path.Combine(folderPath, "repeated.txt"), repeatedNumbers);
                await System.IO.File.WriteAllLinesAsync(Path.Combine(folderPath, "banned.txt"), bannedNumbers);

                order.LoadedCount = numbersList.Count;
                order.InvalidCount = invalidNumbers.Count;
                order.BlacklistedCount = blacklistedNumbers.Count;
                order.RepeatedCount = repeatedNumbers.Count;
                order.BannedCount = bannedNumbers.Count;
              

                await _context.SaveChangesAsync();
                var globalPricing = await _context.Pricing.FirstOrDefaultAsync();
                if (globalPricing == null)
                {
                    return BadRequest("Global pricing configuration is missing.");
                }
                decimal low = company.LowPrice ?? globalPricing.Low;
                decimal medium = company.MediumPrice ?? globalPricing.Middle;
                decimal high = company.HighPrice ?? globalPricing.High;

                decimal pricePerSms;
                if (validNumbers.Count <= 500_000)
                    pricePerSms = low;
                else if (validNumbers.Count <= 1_000_000)
                    pricePerSms = medium;
                else
                    pricePerSms = high;


                var totalCost = validNumbers.Count * pricePerSms;
                var availableBalance = company.CreditLimit;

                // Check balance
                if (availableBalance < totalCost)
                {
                    order.CurrentStatus = "Failed";
                    order.ApiErrorResponse = _sharedLocalizer["insufficientbal"];
                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Sending failed",
                        Message = _sharedLocalizer["insufficientbal"],
                        CreatedAt = TimeHelper.NowInTurkey()
                    });
                    await _context.SaveChangesAsync();

                    return BadRequest(_sharedLocalizer["insufficientbal"]);
                }

                if (order.CurrentStatus == "Scheduled")
                {
                    company.CreditLimit -= totalCost;
                    order.PricePerSms = pricePerSms;
                    order.TotalPrice = totalCost;

                    _context.BalanceHistory.Add(new BalanceHistory
                    {
                        CompanyId = company.CompanyId,
                        Amount = -totalCost,
                        Action = "Deduct on Send (Scheduled)",
                        CreatedAt = TimeHelper.NowInTurkey(),
                        CreatedByUserId = userId
                    });
                    if (!isMainUser && user.QuotaType == "Variable Quota")
                    {
                        _context.Users.Update(user);
                    }
                    await _context.SaveChangesAsync();

                    return Ok(new
                    {

                        message = _sharedLocalizer["smssuccess", order.ScheduledSendDate.GetValueOrDefault().ToString("yyyy-MM-dd HH:mm")],
                        orderId = order.OrderId
                    });
                }
                if (validNumbers.Count == 0)
                {
                    order.CurrentStatus = "Failed";
                    order.ApiErrorResponse = _sharedLocalizer["novalidreceipents"];

                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Sending failed",
                        Message = _sharedLocalizer["novalidreceipents"],
                        CreatedAt = TimeHelper.NowInTurkey()
                    });

                    await _context.SaveChangesAsync();

                    return BadRequest("Geçerli alıcı bulunamadı. SMS gönderilemiyor.");
                }


                // If not trusted → do not send now
                if (!company.IsTrustedSender)
                {
                    return Ok(new
                    {
                        message = _sharedLocalizer["ordercreeatedadminapproval"],
                        orderId = order.OrderId
                    });
                }

                // Deduct balance
                company.CreditLimit -= totalCost;

                // Save in order
                order.PricePerSms = pricePerSms;
                order.TotalPrice = totalCost;

                // Add to BalanceHistory
                _context.BalanceHistory.Add(new BalanceHistory
                {
                    CompanyId = company.CompanyId,
                    Amount = -totalCost,
                    Action = "Deduct on Send",
                    CreatedAt = TimeHelper.NowInTurkey(),
                    CreatedByUserId = userId
                });

                _context.Companies.Update(company);
                await _context.SaveChangesAsync();
                var requestBody = BuildRequestBody(model, api);

                using var httpClient = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Post, api.ApiUrl)
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, api.ContentType)
                };

                var response = await httpClient.SendAsync(request);
                var result = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"API call failed: {response.StatusCode} - {result}");
                    var errorMessage = $"HTTP {(int)response.StatusCode} - {result}";

                    order.ApiErrorResponse = errorMessage;
                    order.CurrentStatus = "Failed";

                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Sending failed",
                        Message = errorMessage,
                        CreatedAt = TimeHelper.NowInTurkey()
                    });
                    if (order.TotalPrice > 0 && order.Returned == false)
                    {
                       // company.CreditLimit += order.TotalPrice.Value;

                        order.Refundable = true;
                        order.Returned = true;
                        order.ReturnDate = TimeHelper.NowInTurkey();

                        _context.BalanceHistory.Add(new BalanceHistory
                        {
                            CompanyId = company.CompanyId,
                            Amount = order.TotalPrice.Value,  // ✅ FIXED
                            Action = "Refund on Failed",
                            CreatedAt = TimeHelper.NowInTurkey(),
                            CreatedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0
                        });
                    }
                    await _context.SaveChangesAsync();

                    return StatusCode((int)response.StatusCode, "SMS API call failed.");
                }

                var json = JsonConvert.DeserializeObject<dynamic>(result);

                if (json.Status == "OK")
                {
                    order.SmsOrderId = json.MessageId;
                    order.StartedAt = TimeHelper.NowInTurkey();
                    order.ReportLock = true;
                    order.CompletedAt = TimeHelper.NowInTurkey();
                    order.ScheduledSendDate = TimeHelper.NowInTurkey();

                    order.CurrentStatus = "Sent";

                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Waiting to be sent",
                        CreatedAt = TimeHelper.NowInTurkey()
                    });
                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Shipping has started",
                        CreatedAt = TimeHelper.NowInTurkey()
                    });
                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Sent",
                        Message = $"MessageId: {json.MessageId}"
                    });
                    await System.IO.File.WriteAllLinesAsync(Path.Combine(folderPath, "processed.txt"), validNumbers);
                    order.ProcessedCount = validNumbers.Count;
                    if (!isMainUser && user.QuotaType == "Variable Quota")
                    {
                        _context.Users.Update(user);
                    }
                    await _context.SaveChangesAsync();

                    return Ok(new
                    {
                        message = _sharedLocalizer["smssent"],
                        messageId = json.MessageId
                    });
                }
                else
                {
                    order.ApiErrorResponse = $"Status: {json.Status}, Full Response: {result}";
                    order.CurrentStatus = "Failed";

                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Sending failed",
                        Message = $"Status: {json.Status}, Full Response: {result}",
                        CreatedAt = TimeHelper.NowInTurkey()
                    });

                    //// 🚀 ADD REFUND HERE TOO!
                    //if (order.TotalPrice > 0 && order.Returned == false)
                    //{
                    //    company.CurrentBalance += order.TotalPrice.Value;

                    //    order.Refundable = true;
                    //    order.Returned = true;
                    //    order.ReturnDate = DateTime.UtcNow.AddHours(3);

                    //    _context.BalanceHistory.Add(new BalanceHistory
                    //    {
                    //        CompanyId = company.CompanyId,
                    //        Amount = order.TotalPrice.Value,
                    //        Action = "Refund on Failed",
                    //        CreatedAt = DateTime.UtcNow.AddHours(3),
                    //        CreatedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0
                    //    });
                    //}

                    await _context.SaveChangesAsync();

                    return BadRequest($"SMS API bir hata döndürdü: {json.Status}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception occurred: {ex.Message}");
                return StatusCode(500, _sharedLocalizer["erroroccuredsendingsms"]);
            }
        }
        [HttpGet]
        public IActionResult GetAvailableApisForOrder(int orderId)
        {
            var order = _context.Orders.Include(o => o.Api).FirstOrDefault(o => o.OrderId == orderId);
            if (order == null) return NotFound();

            var allApis = _context.Apis
                .Where(a => a.IsActive && !a.IsClientApi)
                .Select(a => new { a.ApiId, a.ServiceName })
                .ToList();

            return Json(allApis);
        }

        [HttpPost]
        public async Task<IActionResult> ChangeApiForOrder(int orderId, int newApiId)
        {
            var order = await _context.Orders.FindAsync(orderId);
            var newApi = await _context.Apis.FindAsync(newApiId);

            if (order == null || newApi == null)
                return BadRequest(new { success = false, message = _sharedLocalizer["invalidorder"] });

            order.ApiId = newApiId;
            order.Actions.Add(new OrderAction
            {
                ActionName = "API Changed",
                Message = _sharedLocalizer["changedapi", newApi.ServiceName],
                CreatedAt = TimeHelper.NowInTurkey()
            });

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = _sharedLocalizer["smsapisuccess"] });
        }
        [HttpPost]
        public async Task<IActionResult> ResendOrder(int orderId, DateTime? scheduledDate)
        {
            var order = await _context.Orders
                .Include(o => o.Company)
                .Include(o => o.Api)
                .Include(o => o.CreatedByUser)
                .Include(o => o.Actions.OrderBy(a => a.CreatedAt))
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null ||
                (order.CurrentStatus != "Failed" && order.CurrentStatus != "Sending failed" && order.CurrentStatus != "Submission failed"))
            {
                return Json(new { success = false, message = _sharedLocalizer["ordernoteligible"] });
            }
            var customRecipients = await _context.OrderRecipients
          .Where(r => r.OrderId == orderId)
          .ToListAsync();
            try
            {
                // Build request body from stored order
                var isAzure = Environment.GetEnvironmentVariable("HOME") != null;

                var baseFolderPath = isAzure
                    ? Path.Combine("D:\\home\\data", "orders")
                    : Path.Combine(System.IO.Directory.GetCurrentDirectory(), "App_Data", "orders");
                string fullFilePath;
                if (!string.IsNullOrWhiteSpace(order.FilePath))
                {
                    fullFilePath = isAzure
                        ? Path.Combine("D:\\home", order.FilePath.Replace("/", "\\"))
                        : Path.Combine(System.IO.Directory.GetCurrentDirectory(), "App_Data", order.FilePath.Replace("/", "\\"));
                }
                else
                {
                    // Build it manually from order ID if FilePath not stored
                    fullFilePath = Path.Combine(baseFolderPath, order.OrderId.ToString(), "recipients.txt");
                }
                List<(string Name, string Number)> toSend;
                if (customRecipients.Any())
                {
                    toSend = customRecipients
                      .Select(r => (r.RecipientName, r.RecipientNumber))
                      .ToList();
                }
                else
                {
                    // fallback to your old file‑based list
                    if (!System.IO.File.Exists(fullFilePath))
                        return BadRequest("Recipient file not found.");

                    var raw = await System.IO.File.ReadAllLinesAsync(fullFilePath);
                    toSend = raw
                      .Where(n => !string.IsNullOrWhiteSpace(n))
                      .Select(n => ("", n.Trim()))
                      .ToList();

                    if (!toSend.Any())
                    {
                        order.CurrentStatus = "Failed";
                        order.ApiErrorResponse = "No valid recipients.";
                        order.Actions.Add(new OrderAction
                        {
                            ActionName = "Sending failed",
                            Message = _sharedLocalizer["novalidreceipents"],
                            CreatedAt = TimeHelper.NowInTurkey()
                        });
                        await _context.SaveChangesAsync();
                        return BadRequest("No valid recipients. Cannot send SMS.");
                    }
                }

                if (scheduledDate.HasValue && scheduledDate.Value > TimeHelper.NowInTurkey())
                {
                    order.CurrentStatus = "Scheduled";
                    order.ScheduledSendDate = scheduledDate.Value;
                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Scheduled",
                        Message = _sharedLocalizer["scheduledfor", scheduledDate.Value.ToString("yyyy-MM-dd HH:mm")],
                        CreatedAt = TimeHelper.NowInTurkey()
                    });

                    await _context.SaveChangesAsync();

                    return Json(new
                    {
                        success = true,
                        message = _sharedLocalizer["ordersuccessresend", scheduledDate.Value.ToString("yyyy-MM-dd HH:mm")]

                    });
                }
                var plainNumbers = toSend.Select(r => r.Number).ToArray();
                var isCustom = !string.IsNullOrEmpty(order.PlaceholderColumn);
                var api = order.Api;
                var company = order.Company;
                var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
                var segmentsPerMessage = order.SmsCount > 0 ? order.SmsCount : 1;
                if (segmentsPerMessage <= 0) segmentsPerMessage = 1;
                var globalPricing = await _context.Pricing.FirstOrDefaultAsync();
                if (globalPricing == null)
                    return BadRequest("Global pricing configuration is missing.");
                var totalSmsCredits = toSend.Count * segmentsPerMessage;

                decimal low = company.LowPrice ?? globalPricing.Low;
                decimal medium = company.MediumPrice ?? globalPricing.Middle;
                decimal high = company.HighPrice ?? globalPricing.High;

                decimal pricePerSms = totalSmsCredits <= 500_000 ? low : (totalSmsCredits <= 1_000_000 ? medium : high);
                var totalCost = totalSmsCredits * pricePerSms;

                if (company.CreditLimit < totalSmsCredits)
                {
                    order.CurrentStatus = "Failed";
                    order.ApiErrorResponse = "Insufficient balance.";
                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Sending failed",
                        Message = _sharedLocalizer["insufficientbal"],
                        CreatedAt = TimeHelper.NowInTurkey()
                    });
                    await _context.SaveChangesAsync();
                    return BadRequest("Insufficient balance.");
                }

                string requestBody;
                if (isCustom)
                {
                    var placeholder = $"{{{order.PlaceholderColumn}}}";
                    // personalized (“Messages” array) payload
                    if (api.ServiceName.Equals("turkcell", StringComparison.OrdinalIgnoreCase))
                    {
                        requestBody = JsonConvert.SerializeObject(new
                        {
                            From = api.Originator,
                            User = api.Username,
                            Pass = api.Password,
                            Message = order.MessageText,       // global subject
                            StartDate = (string)null,
                            ValidityPeriod = 1440,
                            Messages = toSend.Select(r => new {
                                Message = order.MessageText
                                                 .Replace(placeholder, r.Name),
                                GSM = r.Number
                            }).ToArray()
                        });
                    }
                    else // assume Yurtici or other JSON APIs
                    {
                        requestBody = JsonConvert.SerializeObject(new
                        {
                            Username = api.Username,
                            Password = api.Password,
                            Messages = toSend.Select(r => new {
                                To = r.Number,
                                Text = order.MessageText
                                           .Replace($"{{{order.PlaceholderColumn}}}", r.Name)
                            }).ToArray()
                        });
                    }
                }
                else
                {
                    // bulk “flat” payload exactly as you had before
                    if (api.ServiceName.Equals("turkcell", StringComparison.OrdinalIgnoreCase))
                    {
                        requestBody = JsonConvert.SerializeObject(new
                        {
                            User = api.Username,
                            Pass = api.Password,
                            Message = order.MessageText,
                            Numbers = plainNumbers
                        });
                    }
                    else if (api.ContentType == "application/json")
                    {
                        requestBody = JsonConvert.SerializeObject(new
                        {
                            Username = api.Username,
                            Password = api.Password,
                            Text = order.MessageText,
                            To = plainNumbers
                        });
                    }
                    else
                    {
                        return BadRequest("Unsupported API format.");
                    }
                }


                using var client = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Post, api.ApiUrl)
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, api.ContentType)
                };



                var stopwatch = Stopwatch.StartNew();
                var response = await client.SendAsync(request);
                stopwatch.Stop();
                var result = await response.Content.ReadAsStringAsync();
                string sanitizedBody = requestBody;


                sanitizedBody = Regex.Replace(sanitizedBody, @"\d{10,15}", "[NUMBER]");

                _context.ApiCallLogs.Add(new ApiCallLog
                {
                    CompanyId = order.CompanyId,
                    UserId = order.CreatedByUserId,
                    ApiUrl = api.ApiUrl,
                    OrderId = order.OrderId,
                    RequestBody = sanitizedBody,
                    ResponseContent = result,
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    CreatedAt = TimeHelper.NowInTurkey()
                });
                await _context.SaveChangesAsync();

                if (!response.IsSuccessStatusCode)
                {
                    order.ApiErrorResponse = $"HTTP {(int)response.StatusCode} - {result}";
                    order.CurrentStatus = "Failed";

                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Sending failed",
                        Message = order.ApiErrorResponse,
                        CreatedAt = TimeHelper.NowInTurkey()
                    });

                    await _context.SaveChangesAsync();

                    return BadRequest($"SMS API call failed: {result}");
                }

                dynamic json = JsonConvert.DeserializeObject(result);

                if (json.Status == "OK")
                {
                    order.SmsOrderId = json.MessageId;
                    order.StartedAt = TimeHelper.NowInTurkey();
                    order.ReportLock = true;
                    order.CompletedAt = TimeHelper.NowInTurkey();
                    order.ScheduledSendDate = TimeHelper.NowInTurkey();
                    order.CurrentStatus = "Sent";

                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Resent",
                        CreatedAt = TimeHelper.NowInTurkey()
                    });
                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Sent",
                        Message = $"MessageId: {json.MessageId}",
                        CreatedAt = TimeHelper.NowInTurkey()
                    });

                    await _context.SaveChangesAsync();

                    return Json(new { success = true, message = _sharedLocalizer["orderresentsms"] });
                }
                else
                {
                    order.ApiErrorResponse = $"Status: {json.Status}, Full Response: {result}";
                    order.CurrentStatus = "Failed";

                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Sending failed",
                        Message = order.ApiErrorResponse,
                        CreatedAt = TimeHelper.NowInTurkey()
                    });

                    await _context.SaveChangesAsync();

                    return BadRequest($"SMS API returned an error: {json.Status}");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Exception occurred: {ex.Message}");
            }
        }
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            // ✅ Authorization check
            (bool isAuthorized, Order? order, string? errorMessage) = await CanAccessOrderAsync(id);

            if (order == null)
                return NotFound(errorMessage);

            // ✅ Additional check: Panel users with Edit permission can access
            if (!isAuthorized && HasAccessRoles("Order", "Edit"))
            {
                isAuthorized = true;
            }

            if (!isAuthorized)
                return Forbid();

            // Load related data
            await _context.Entry(order).Reference(o => o.Company).LoadAsync();
            await _context.Entry(order).Reference(o => o.Api).LoadAsync();
            await _context.Entry(order).Reference(o => o.CreatedByUser).LoadAsync();
            await _context.Entry(order).Collection(o => o.Actions).LoadAsync();

            return View(order);
        }
        [HttpGet]
        public async Task<IActionResult> GetOrderDetails(int id)
        {
            // ✅ Authorization check
            (bool isAuthorized, Order? order, string? errorMessage) = await CanAccessOrderAsync(id);

            if (order == null)
                return NotFound(errorMessage);

            if (!isAuthorized)
                return Forbid();

            // Load related data
            await _context.Entry(order).Reference(o => o.Company).LoadAsync();
            await _context.Entry(order).Reference(o => o.Api).LoadAsync();
            await _context.Entry(order).Reference(o => o.CreatedByUser).LoadAsync();
            await _context.Entry(order).Collection(o => o.Actions).LoadAsync();

            var model = new OrderDetailsViewModel
            {
                OrderId = order.OrderId,
                LoadedCount = order.LoadedCount,
                ProcessedCount = order.ProcessedCount,
                InvalidCount = order.InvalidCount,
                RepeatedCount = order.RepeatedCount,
                BannedCount = order.BannedCount,
                BlacklistedCount = order.BlacklistedCount,
                UnsuccessfulCount = order.UnsuccessfulCount,
                TotalCount = order.TotalCount,
                DeliveredCount = order.DeliveredCount,
                UndeliveredCount = order.UndeliveredCount,
                WaitingCount = order.WaitingCount,
                ExpiredCount = order.ExpiredCount,
                CurrentStatus = order.CurrentStatus,
                CreatedAt = order.CreatedAt,
                StartedAt = order.StartedAt,
                ReportedAt = order.ReportedAt,
                MessageText = order.MessageText,
                CompanyName = order.Company?.CompanyName,
                ApiName = order.Api?.ServiceName,
                SubmissionType = order.SubmissionType,
                CreatedByUserFullName = order.CreatedByUser?.FullName,
                ReportLock = order.ReportLock
            };

            return PartialView("_OrderDetailsPartial", order);
        }
        [HttpPost]
        public async Task<IActionResult> CancelOrder(int orderId)
        {
            var order = await _context.Orders
                .Include(o => o.Company)
                .Include(o => o.CreatedByUser)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
            {
                return NotFound(new { messageKey = "OrderNotFound" });
            }

            if (order.CurrentStatus == "Sent" || order.CurrentStatus == "Cancelled")
            {
                return BadRequest(new { messageKey = "OrderCannotBeCancelled" });
            }

            // 1️⃣ Calculate refund amount
            var refundAmount = order.TotalPrice ?? 0;

            // 2️⃣ Refund to company (if credits were deducted)
            if (refundAmount > 0)
            {
                order.Company.CreditLimit += refundAmount;

                // 3️⃣ Create BalanceHistory entry
                _context.BalanceHistory.Add(new BalanceHistory
                {
                    CompanyId = order.CompanyId,
                    Amount = refundAmount,
                    Action = "Refund on Cancel",
                    OrderId = order.OrderId,
                    CreatedAt = TimeHelper.NowInTurkey(),
                    CreatedByUserId = order.CreatedByUserId
                });

                // 4️⃣ Create CreditTransaction entry
                _context.CreditTransactions.Add(new CreditTransaction
                {
                    CompanyId = order.CompanyId,
                    Credit = refundAmount,
                    TransactionDate = TimeHelper.NowInTurkey(),
                    Note = $"Sipariş iptali - Order #{order.OrderId}",
                    UnitPrice = 0,
                    TotalPrice = 0
                });

                // 5️⃣ Restore user quota (if variable)
                if (order.CreatedByUser != null && order.CreatedByUser.QuotaType == "Variable Quota")
                {
                    order.CreatedByUser.Quota = (order.CreatedByUser.Quota ?? 0) + (int)refundAmount;
                }
            }

            // 6️⃣ Update order status
            order.CurrentStatus = "Cancelled";
            order.UpdatedAt = TimeHelper.NowInTurkey();

            order.Actions.Add(new OrderAction
            {
                ActionName = "Cancelled",
                Message = refundAmount > 0
                    ? $"Order cancelled. {refundAmount} credits refunded."
                    : "Order cancelled.",
                CreatedAt = TimeHelper.NowInTurkey()
            });

            await _context.SaveChangesAsync();

            // 7️⃣ SignalR notifications
            await _hubContext.Clients.Group("Admins").SendAsync("OrderStatusChanged", new { orderId = order.OrderId, newStatus = "Cancelled" });
            await _hubContext.Clients.Group("PanelUsers").SendAsync("OrderStatusChanged", new { orderId = order.OrderId, newStatus = "Cancelled" });
            await _hubContext.Clients.Group($"company_{order.CompanyId}").SendAsync("OrderStatusChanged", new { orderId = order.OrderId, newStatus = "Cancelled" });
            await _hubContext.Clients.Group($"user_{order.CreatedByUserId}").SendAsync("OrderStatusChanged", new { orderId = order.OrderId, newStatus = "Cancelled" });

            return Ok(new { messageKey = "OrderCancelledSuccessfully", refundedAmount = refundAmount });
        }
    }
}