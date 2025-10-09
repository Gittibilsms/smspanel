using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using GittBilSmsCore.ViewModels;
using System.Text;
using Newtonsoft.Json;
using GittBilSmsCore.Helpers;
using System.IO;
using ClosedXML.Excel;    
using CompanyModel = GittBilSmsCore.Models.Company;
using DocumentFormat.OpenXml.Spreadsheet;
using ClosedXML;
using GittBilSmsCore.Resources;
using Microsoft.AspNetCore.Identity;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Net.Http;
using CsvHelper;
using System.Globalization;
using System;
using Newtonsoft.Json.Linq;
using DocumentFormat.OpenXml.Wordprocessing;
using GittBilSmsCore.Services;
using GittBilSmsCore.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Threading;
using GittBilSmsCore.Hubs;
using Microsoft.AspNetCore.SignalR;
namespace GittBilSmsCore.Controllers
{
    public class HomeController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        //private readonly IStringLocalizer<HomeController> _localizer;
        private readonly INotificationService _notificationService;
        private readonly IStringLocalizer _sharedLocalizer;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly UserManager<User> _userManager;
        public HomeController(GittBilSmsDbContext context, IStringLocalizerFactory factory, INotificationService notificationService, IHubContext<ChatHub> hubContext, UserManager<User> userManager, IWebHostEnvironment env) : base(context)
        {
            _context = context;
            _hubContext = hubContext;
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
            _notificationService = notificationService;
            _userManager = userManager;
            _env = env;
        }

        public async Task<IActionResult> Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            var userType = HttpContext.Session.GetString("UserType") ?? "";
            var companyId = HttpContext.Session.GetInt32("CompanyId");

            List<Company> companies;
            Company firstCompany;

            if (userType == "CompanyUser" && companyId.HasValue)
            {
                companies = await _context.Companies
                    .Where(c => c.CompanyId == companyId.Value && c.IsActive)
                    .ToListAsync();
                firstCompany = companies.FirstOrDefault();
            }
            else
            {
                companies = await _context.Companies
                    .Where(c => c.IsActive)
                    .ToListAsync();
                firstCompany = companies.FirstOrDefault();
            }

            // 🚀 Load APIs
            var apis = await _context.Apis
                .Where(a => a.IsActive && !a.IsClientApi)
                .ToListAsync();

            var defaultApiId = apis.FirstOrDefault(a => a.IsDefault)?.ApiId;

            var user = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => new { u.FullName, u.ProfilePhotoUrl })
                .FirstOrDefaultAsync();
      
            ViewBag.UserName = user?.FullName ?? "Unknown User";
            ViewBag.ProfilePhotoUrl = string.IsNullOrEmpty(user?.ProfilePhotoUrl)
                ? "/assets/images/avatars/01.png"
                : user.ProfilePhotoUrl;

            var model = new HomeIndexViewModel
            {
                Companies = companies,
                ApiLists = apis.Select(a => new SelectListItem
                {
                    Value = a.ApiId.ToString(),
                    Text = a.ServiceName
                }).ToList(),
                DefaultApiId = defaultApiId,
                LowPrice = firstCompany?.LowPrice ?? 0,
                MediumPrice = firstCompany?.MediumPrice ?? 0,
                HighPrice = firstCompany?.HighPrice ?? 0,
                Company = firstCompany
            };

            return View(model);
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
        [HttpPost]
        public async Task<IActionResult> Login(string email, string password)
        {
            var user = await _context.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Email == email && u.Password == password && u.IsActive == true);

            if (user != null)
            {
                HttpContext.Session.SetInt32("UserId", user.Id);
                HttpContext.Session.SetString("UserType", user.UserType ?? "Unknown");

                if (user.CompanyId != null && user.CompanyId > 0)
                    HttpContext.Session.SetInt32("CompanyId", user.CompanyId.Value);

                var roleIds = user.UserRoles.Select(ur => ur.RoleId).ToList();
                HttpContext.Session.SetString("RoleIds", string.Join(",", roleIds));

                Console.WriteLine($"User '{user.UserName}' logged in. UserType: {user.UserType}");

                return Ok();
            }

            return BadRequest("Geçersiz kullanıcı adı veya şifre.");
        }
        public IActionResult SendSms()
        {
            var apis = _context.Apis.Where(a => a.IsActive && !a.IsClientApi).ToList();
            ViewBag.ApiList = new SelectList(apis, "ApiId", "ServiceName");
            return View();
        }
        // Download Report Summary
        [HttpGet]
        public IActionResult DownloadReportSummary(int orderId)
        {
            return DownloadReportFile(orderId, "report-summary.csv", "Summary");
        }

        // Download Undelivered
        [HttpGet]
        public IActionResult DownloadUndelivered(int orderId)
        {
            return DownloadReportFile(orderId, "undelivered.csv", "Undelivered");
        }

        // Download Forwarded
        [HttpGet]
        public IActionResult DownloadWaiting(int orderId)
        {
            return DownloadReportFile(orderId, "waiting.csv", "Waiting");
        }

        [HttpGet]
        public IActionResult DownloadAllReport(int orderId)
        {
            return DownloadReportFile(orderId, "all.csv", "All");
        }
        // Download Waiting
        [HttpGet]
        public IActionResult DownloadForwarded(int orderId)
        {
            return DownloadReportFile(orderId, "delivered.csv", "Forwarded");
        }

        // Download Expired
        [HttpGet]
        public IActionResult DownloadExpired(int orderId)
        {
            return DownloadReportFile(orderId, "expired.csv", "Expired");
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

            return Ok(new {success = true, message = _sharedLocalizer["smsapisuccess"] });
        }
        public class RecipientDto
        {
            public string Name { get; set; }
            public string Number { get; set; }
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

                List<(string Name, string Number)> recipients;

                // 1) STANDARD‐TEXTAREA PATH
                if (model.FileMode == "standard"
                    && !string.IsNullOrWhiteSpace(model.PhoneNumbers))
                {
                    recipients = model.PhoneNumbers
                       .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(n => ("", n.Trim()))
                       .ToList();
                }
                // 2) CUSTOM‐FILE PATH
                else if (model.FileMode == "custom"
                         && model.files?.Length > 0)
                {
                    recipients = ParseRecipientsFromFile(
                        model.files[0],
                        model.HasName,
                        model.SelectedCustomColumnKey,
                        model.SelectedNumberColumnKey
                    );
                }
                // 3) PERSONALIZED JSON PATH
                else if (model.FileMode == "custom"
                         && !string.IsNullOrWhiteSpace(model.RecipientsJson))
                {
                    var list = JsonConvert
                                 .DeserializeObject<List<RecipientDto>>(model.RecipientsJson);
                    recipients = list.Select(r => (r.Name, r.Number)).ToList();
                }
                else
                {
                    return BadRequest("No recipients provided.");
                }

                // 3️⃣ Build payload
                bool isPersonalized =
                   !string.IsNullOrWhiteSpace(model.RecipientsJson)
                   && model.RecipientsJson.Length > 1;

                object payload;

                var rawNumbers = model.PhoneNumbers ?? "";
                //var numbersList = rawNumbers
                //    .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                //    .Select(n => n.Trim())
                //    .Where(n => !string.IsNullOrWhiteSpace(n))
                //    .ToList();
                var numbersList = recipients.Select(r => r.Number).ToList();
                int segmentsPerMessage = model.TotalSmsCount ?? 0;
              

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
                    SmsCount = segmentsPerMessage,
                    CreatedAt = TimeHelper.NowInTurkey()
                };
                order.LoadedCount = recipients.Count;

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

                var payloadsignalrOrder = new
                {
                    orderId = order.OrderId,
                    status = order.CurrentStatus,
                    companyName = company.CompanyName,
                    dateOfSending = (order.CurrentStatus == "Scheduled" && order.ScheduledSendDate.HasValue)
                                       ? order.ScheduledSendDate.Value.ToString("yyyy-MM-dd HH:mm")
                                       : order.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    createdAt = order.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    apiName = api.ServiceName,
                    submissionType = order.SubmissionType,
                    loadedCount = order.LoadedCount,
                    processedCount = order.ProcessedCount,
                    unsuccessfulCount = order.UnsuccessfulCount,
                    createdBy = user.UserName,
                    refundable = order.Refundable,
                    returned = order.Returned,
                    returnDate = order.ReturnDate?.ToString("yyyy-MM-dd HH:mm")
                };
                var httpUser = HttpContext.User;
                // 1) Always notify all Admins
                await _hubContext.Clients
                    .Group("Admins")
                    .SendAsync("ReceiveNewOrder", payloadsignalrOrder);

                await _hubContext.Clients
                  .Group("PanelUsers")
                  .SendAsync("ReceiveNewOrder", payloadsignalrOrder);

                // 2) Company‐level users
                if (httpUser.IsInRole("CompanyUser"))
                {
                    var MainUser = (HttpContext.Session.GetInt32("IsMainUser") ?? 0) == 1;

                    if (MainUser)
                    {
                        // main user = sees all company orders
                        await _hubContext.Clients
                            .Group($"company_{order.CompanyId}")
                            .SendAsync("ReceiveNewOrder", payloadsignalrOrder);
                    }
                    else
                    {
                        // sub‐user = only their own
                        await _hubContext.Clients
                            .Group($"user_{order.CreatedByUserId}")
                            .SendAsync("ReceiveNewOrder", payloadsignalrOrder);
                    }
                }
                if (!company.IsTrustedSender)
                {
                    var notif = new Notifications
                    {
                        Title = _sharedLocalizer["NewOrderAwaitingApproval"],
                        Description = string.Format(_sharedLocalizer["OrderAwaitingAdminApprovalDesc"], order.OrderId),
                        Type = NotificationType.SmsAwaitingApproval,
                        CreatedAt = TimeHelper.NowInTurkey(),
                        IsRead = false,
                        CompanyId = company.CompanyId,
                        OrderId = order.OrderId,
                        UserId = user.Id
                    };
                    await _notificationService.AddNotificationAsync(notif);
                    var newNotificationId = notif.NotificationId;
                    var signalRpayload = new
                    {
                        notificationId = newNotificationId,
                        title = _sharedLocalizer["NewOrderAwaitingApproval"],
                        description = string.Format(_sharedLocalizer["OrderAwaitingAdminApprovalDesc"], order.OrderId),
                        type = (int)NotificationType.SmsAwaitingApproval,
                        createdAt = TimeHelper.NowInTurkey(),
                        companyId = company.CompanyId,
                        orderId = order.OrderId,
                        userId = user.Id
                    };

                    await _hubContext.Clients.Group("Admins")
                        .SendAsync("ReceiveNotification", signalRpayload);

                    await _hubContext.Clients
                         .Group("PanelUsers")
                         .SendAsync("ReceiveNotification", signalRpayload);
                            }

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
                    num = Regex.Replace(num, @"\D", ""); // keep only digits

                    if (num.StartsWith("90") && num.Length == 12) return true;   // 905xxxxxxxxx
                    if (num.StartsWith("0") && num.Length == 11) return true;    // 05xxxxxxxxx
                    if (num.StartsWith("5") && num.Length == 10) return true;    // 5xxxxxxxxx

                    return false;
                }

                string NormalizePhone(string num)
                {
                    num = Regex.Replace(num, @"\D", ""); // digits only

                    if (num.StartsWith("90") && num.Length == 12) return num;
                    if (num.StartsWith("0") && num.Length == 11) return "90" + num.Substring(1); // convert 05 → 905
                    if (num.StartsWith("5") && num.Length == 10) return "90" + num;              // convert 5 → 905
                    return num; // fallback (shouldn't hit if validated)
                }

                foreach (var number in numbersList)
                {
                    var digitsOnly = Regex.Replace(number, @"\D", "");

                    if (!IsValidPhone(digitsOnly))
                    {
                        invalidNumbers.Add(number); // keep original for invalid.txt
                        continue;
                    }

                    var normalized = NormalizePhone(digitsOnly); // always unify to 905xxxxxxxxx

                    if (blacklist.Contains(normalized))
                    {
                        blacklistedNumbers.Add(normalized);
                        continue;
                    }

                    if (banned.Contains(normalized))
                    {
                        bannedNumbers.Add(normalized);
                        continue;
                    }

                    if (seenNumbers.Contains(normalized))
                    {
                        repeatedNumbers.Add(normalized);
                        continue;
                    }

                    validNumbers.Add(normalized);
                    seenNumbers.Add(normalized);
                }

                // Now recipients need to be checked with normalized numbers
                var validRecipients = recipients
                    .Where(r => validNumbers.Contains(NormalizePhone(r.Number)))
                    .ToList();

                if (!validRecipients.Any())
                    return BadRequest("No valid recipients.");
                if (!string.IsNullOrWhiteSpace(model.RecipientsJson))
                {
                    var orderRecipientEntities = validRecipients
                      .Select(r => new OrderRecipient
                      {
                          OrderId = order.OrderId,
                          RecipientName = r.Name,
                          RecipientNumber = r.Number
                      })
                      .ToList();
                    _context.OrderRecipients.AddRange(orderRecipientEntities);
                    await _context.SaveChangesAsync();
                }
                order.PlaceholderColumn = model.SelectedCustomColumn;
                int totalSmsCredits = validNumbers.Count * segmentsPerMessage;

                bool isMainUser = user.IsMainUser ?? false;

                if (!isMainUser && user.QuotaType == "Variable Quota")
                {
                    int allowedQuota = user.Quota ?? 0;

                    if (allowedQuota <= 0 || totalSmsCredits > allowedQuota)
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

                        return BadRequest(new { value = _sharedLocalizer["quotanotavailable"] });
                    }

                    // ✅ Deduct later only if actually sent/scheduled
                    user.Quota = allowedQuota - totalSmsCredits;
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
                if (totalSmsCredits <= 500_000)
                    pricePerSms = low;
                else if (totalSmsCredits <= 1_000_000)
                    pricePerSms = medium;
                else
                    pricePerSms = high;


                var totalCost = totalSmsCredits * pricePerSms;
                var availableBalance = company.CreditLimit;

                // Check balance
                if (availableBalance < totalSmsCredits)
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
                    company.CreditLimit -= totalSmsCredits;
                    order.PricePerSms = pricePerSms;
                    order.TotalPrice = totalSmsCredits;

                    _context.BalanceHistory.Add(new BalanceHistory
                    {
                        CompanyId = company.CompanyId,
                        Amount = -totalSmsCredits,
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
                company.CreditLimit -= totalSmsCredits;

                // Save in order
                order.PricePerSms = pricePerSms;
                order.TotalPrice = totalSmsCredits;

                // Add to BalanceHistory
                _context.BalanceHistory.Add(new BalanceHistory
                {
                    CompanyId = company.CompanyId,
                    Amount = -totalSmsCredits,
                    Action = "Deduct on Send",
                    CreatedAt = TimeHelper.NowInTurkey(),
                    CreatedByUserId = userId
                });
    
                _context.Companies.Update(company);
                await _context.SaveChangesAsync();
                bool hasPlaceholder =
                     !string.IsNullOrWhiteSpace(model.SelectedCustomColumn)
                  && model.Message.Contains($"{{{model.SelectedCustomColumn}}}");

                if (hasPlaceholder)
                {
                    model.RecipientsJson = JsonConvert.SerializeObject(
                      validRecipients.Select(r => new RecipientDto { Name = r.Name, Number = r.Number })
                    );
                }
                else
                {
                    model.RecipientsJson = null;
                    model.PhoneNumbers = string.Join(",", validNumbers);
                }

                var originalText = model.Message;
                var originalPhones = model.PhoneNumbers;
                using var httpClient = new HttpClient();

              
                    //// 1) build the personalized message
                    //var template = model.Message;
                    //var placeholder = $"{{{model.SelectedCustomColumn}}}";
                    //var personalized = originalText.Replace(placeholder, rec.Name);
                    //var text = template.Replace(placeholder, rec.Name);
                    //model.Message = personalized;
                    //model.PhoneNumbers = rec.Number;
                    // 2) build the request body
                    var body = BuildRequestBody(model, api);
                    // 3) time the call
                    var stopwatch = Stopwatch.StartNew();

                    var response = await httpClient.SendAsync(
                        new HttpRequestMessage(HttpMethod.Post, api.ApiUrl)
                        {
                            Content = new StringContent(body, Encoding.UTF8, api.ContentType)
                        }
                    );
                    stopwatch.Stop();

                    var result = await response.Content.ReadAsStringAsync();

                    // 4) sanitize & log the raw request/response
                    var sanitizedBody = Regex.Replace(body, @"\d{10,15}", "[NUMBER]");

                    _context.ApiCallLogs.Add(new ApiCallLog
                    {
                        CompanyId = model.CompanyId,
                        UserId = userId,
                        ApiUrl = api.ApiUrl,
                        OrderId = order.OrderId,
                        RequestBody = sanitizedBody,
                        ResponseContent = result,
                        ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                        CreatedAt = TimeHelper.NowInTurkey()
                    });
                    await _context.SaveChangesAsync();

                    // 5) if this single‐number call failed, mark the order failed & refund
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorMessage = $"HTTP {(int)response.StatusCode} - {result}";
                        order.ApiErrorResponse = errorMessage;
                        order.CurrentStatus = "Failed";
                        order.Actions.Add(new OrderAction
                        {
                            ActionName = "Sending failed",
                            Message = errorMessage,
                            CreatedAt = TimeHelper.NowInTurkey()
                        });

                        // refund
                        if (order.TotalPrice > 0 && !order.Returned)
                        {
                            company.CreditLimit += order.TotalPrice.Value;
                            order.Refundable = true;
                            order.Returned = true;
                            order.ReturnDate = TimeHelper.NowInTurkey();

                            _context.BalanceHistory.Add(new BalanceHistory
                            {
                                CompanyId = company.CompanyId,
                                Amount = order.TotalPrice.Value,
                                Action = "Refund on Failed",
                                CreatedAt = TimeHelper.NowInTurkey(),
                                CreatedByUserId = userId
                            });
                        }

                        await _context.SaveChangesAsync();
                        return StatusCode((int)response.StatusCode, "SMS API call failed.");
                    }

                    // 6) if OK, pull out MessageId (if your API returns one per call)
                    var json = JsonConvert.DeserializeObject<dynamic>(result);
                    if (json.Status == "OK")
                    {
                        // you might collect multiple MessageId values, but at minimum store the last one:
                        order.SmsOrderId = json.MessageId;
                    }
                    else
                    {
                        // treat non‑OK as failure
                        var errorMessage = $"Status: {json.Status}, Full Response: {result}";
                        order.ApiErrorResponse = errorMessage;
                        order.CurrentStatus = "Failed";
                        order.Actions.Add(new OrderAction
                        {
                            ActionName = "Sending failed",
                            Message = errorMessage,
                            CreatedAt = TimeHelper.NowInTurkey()
                        });
                        await _context.SaveChangesAsync();
                        return BadRequest($"SMS API bir hata döndürdü: {json.Status}");
                    }
               

                // ─── all calls succeeded ───

                // finalize order
                order.StartedAt = TimeHelper.NowInTurkey();
                order.CompletedAt = TimeHelper.NowInTurkey();
                order.ReportLock = true;
                order.CurrentStatus = "Sent";
                order.Actions.Add(new OrderAction
                {
                    ActionName = "Sent",
                    CreatedAt = TimeHelper.NowInTurkey()
                });
                order.ProcessedCount = validNumbers.Count;

                // write the "processed.txt" file
                await System.IO.File.WriteAllLinesAsync(
                    Path.Combine(folderPath, "processed.txt"),
                    validNumbers
                );

                // update quota & balance history if needed
                if (!isMainUser && user.QuotaType == "Variable Quota")
                    _context.Users.Update(user);

                await _context.SaveChangesAsync();
                var payloadstatus = new
                {
                    orderId = order.OrderId,
                    newStatus = order.CurrentStatus
                };

                // 1) Company-wide (for main users)
                await _hubContext.Clients
                    .Group($"company_{order.CompanyId}")
                    .SendAsync("OrderStatusChanged", payloadstatus);

                await _hubContext.Clients
                .Group("PanelUsers")
                .SendAsync("OrderStatusChanged", payloadstatus);

                // 2) Personal (for sub-users)
                await _hubContext.Clients
                    .Group($"user_{order.CreatedByUserId}")
                    .SendAsync("OrderStatusChanged", payloadstatus);

                // 3) Optionally Admins
                await _hubContext.Clients
                    .Group("Admins")
                    .SendAsync("OrderStatusChanged", payloadstatus);
                // return success to caller
                return Ok(new
                {
                    message = _sharedLocalizer["smssent"],
                    orderId = order.OrderId,
                    // optionally return MessageIds or count here
                });
                model.Message = originalText;
                model.PhoneNumbers = originalPhones;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception occurred: {ex.Message}");
                return StatusCode(500, _sharedLocalizer["erroroccuredsendingsms"]);
            }
        }
        [HttpPost]
        public IActionResult GetColumnValues(IFormFile file, string columnName)
        {
            if (file == null || string.IsNullOrEmpty(columnName))
                return Ok(Array.Empty<string>());

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            string[] firstRow, secondRow = null;
            bool hasSecond = false;

            // 1) Peek first two rows
            if (ext is ".csv" or ".txt")
            {
                using var reader = new StreamReader(file.OpenReadStream());
                using var csv = new CsvHelper.CsvReader(reader, CultureInfo.InvariantCulture);

                if (!csv.Read())
                    return Ok(Array.Empty<string>());
                firstRow = csv.Parser.Record;

                hasSecond = csv.Read();
                if (hasSecond)
                    secondRow = csv.Parser.Record;
            }
            else if (ext is ".xls" or ".xlsx")
            {
                using var stream = file.OpenReadStream();
                using var wb = new ClosedXML.Excel.XLWorkbook(stream);
                var ws = wb.Worksheet(1);

                firstRow = ws.Row(1).CellsUsed().Select(c => c.GetString()).ToArray();
                var lastRow = ws.LastRowUsed().RowNumber();
                if (lastRow >= 2)
                {
                    hasSecond = true;
                    secondRow = ws.Row(2).Cells(1, firstRow.Length).Select(c => c.GetString()).ToArray();
                }
            }
            else
            {
                return BadRequest("Unsupported file type");
            }

            // 2) Header detection
            bool row1Letters = firstRow.Any(cell => Regex.IsMatch(cell, "[A-Za-z]"));
            bool row2Nums = hasSecond && secondRow.All(cell => Regex.IsMatch(cell, @"^[\d\+\-\s\(\)]+$"));
            bool hasHeader = row1Letters && row2Nums;

            List<string> headers = hasHeader
                ? firstRow.ToList()
                : firstRow.Select((_, i) => $"Column_{IndexToLetters(i)}").ToList();

            // 3) Which column?
            int idx = headers.IndexOf(columnName);
            if (idx < 0)
                return Ok(Array.Empty<string>());

            // 4) Read all values from that column
            var values = new List<string>();
            if (ext is ".csv" or ".txt")
            {
                using var reader = new StreamReader(file.OpenReadStream());
                using var csv = new CsvHelper.CsvReader(reader, CultureInfo.InvariantCulture);

                // skip header if present
                csv.Read();
                if (hasHeader) csv.Read();

                while (csv.Read())
                    values.Add(csv.GetField(idx));
            }
            else
            {
                using var stream = file.OpenReadStream();
                using var wb = new ClosedXML.Excel.XLWorkbook(stream);
                var ws = wb.Worksheet(1);

                int startRow = hasHeader ? 2 : 1;
                int lastRow = ws.LastRowUsed().RowNumber();
                for (int r = startRow; r <= lastRow; r++)
                    values.Add(ws.Cell(r, idx + 1).GetString());
            }

            return Ok(values);
        }

        [HttpPost]
        public IActionResult GetColumns(IFormFile file)
        {
            if (file == null)
                return BadRequest("Please select a file.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            string[] firstRow, secondRow = null;
            bool hasSecondRow = false;

            // 1) Read first (and second) row
            if (ext is ".csv" or ".txt")
            {
                using var reader = new StreamReader(file.OpenReadStream());
                using var csv = new CsvHelper.CsvReader(reader, CultureInfo.InvariantCulture);

                if (!csv.Read())
                    return BadRequest("Empty file");
                firstRow = csv.Parser.Record;

                hasSecondRow = csv.Read();
                if (hasSecondRow)
                    secondRow = csv.Parser.Record;
            }
            else if (ext is ".xls" or ".xlsx")
            {
                using var stream = file.OpenReadStream();
                using var wb = new ClosedXML.Excel.XLWorkbook(stream);
                var ws = wb.Worksheet(1);

                firstRow = ws.Row(1)
                             .CellsUsed()
                             .Select(c => c.GetString())
                             .ToArray();

                var lastRow = ws.LastRowUsed().RowNumber();
                if (lastRow >= 2)
                {
                    hasSecondRow = true;
                    secondRow = ws.Row(2)
                                  .Cells(1, firstRow.Length)
                                  .Select(c => c.GetString())
                                  .ToArray();
                }
            }
            else
            {
                return BadRequest("Unsupported file type");
            }

            // 2) Decide if row 1 is really a header:
            //    - row1 has at least one letter
            //    - AND row2 exists and is *all* numeric (digits, +, –, spaces, parentheses)
            bool row1HasLetters = firstRow.Any(f => Regex.IsMatch(f, "[A-Za-z]"));
            bool row2AllNumeric = hasSecondRow
                && secondRow.All(s => Regex.IsMatch(s, @"^[\d\+\-\s\(\)]+$"));

            bool hasHeader = row1HasLetters && row2AllNumeric;

            // 3) Build the column list
            List<string> columns;
            if (hasHeader)
            {
                columns = firstRow.ToList();
            }
            else
            {
                // no header → synthetic Column_A, Column_B, …
                columns = firstRow.Select((_, i) => $"Column_{IndexToLetters(i)}").ToList();
            }

            return Json(columns);
        }

        // helper: 0->A,1->B,…,25->Z,26->AA…
        private static string IndexToLetters(int index)
        {
            string s = "";
            do
            {
                s = (char)('A' + (index % 26)) + s;
                index = index / 26 - 1;
            } while (index >= 0);
            return s;
        }

        [HttpPost]
        public async Task<IActionResult> ChangeApiAndResend(int orderId, int apiId)
        {
            // 1) Load order + related data
            var order = await _context.Orders
                .Include(o => o.Company)
                .Include(o => o.Api)
                .Include(o => o.Actions.OrderBy(a => a.CreatedAt))
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return NotFound("Order not found.");

            // 2) Determine recipient list
            var customRecipients = await _context.OrderRecipients
                .Where(r => r.OrderId == orderId)
                .ToListAsync();

            List<(string Name, string Number)> toSend;
            if (customRecipients.Any())
            {
                // personalized path
                toSend = customRecipients
                    .Select(r => (r.RecipientName, r.RecipientNumber))
                    .ToList();
            }
            else
            {
                // fallback: read the plain numbers file
                var isAzure = Environment.GetEnvironmentVariable("HOME") != null;
                var baseFolder = isAzure
                    ? @"D:\home\data\orders"
                    : Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "orders");
                var folder = Path.Combine(baseFolder, order.OrderId.ToString());
                var filePath = System.IO.File.Exists(Path.Combine(folder, "recipients.txt"))
                    ? Path.Combine(folder, "recipients.txt")
                    : throw new FileNotFoundException("Recipient file not found.", folder);

                var lines = await System.IO.File.ReadAllLinesAsync(filePath);
                toSend = lines
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(n => ("", n.Trim()))
                    .ToList();
            }

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
                return BadRequest("No valid recipients. Cannot resend SMS.");
            }

            // 3) Pull API & company, calculate credits
            var api = await _context.Apis.FirstOrDefaultAsync(a => a.ApiId == apiId && a.IsActive);
            if (api == null)
                return BadRequest("Invalid API selected.");

            var company = order.Company;
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;

            int segments = order.SmsCount ?? 0;
            int totalCredits = toSend.Count * segments;

            var pricing = await _context.Pricing.FirstOrDefaultAsync()
                          ?? throw new Exception("Global pricing configuration is missing.");

            decimal low = company.LowPrice ?? pricing.Low;
            decimal mid = company.MediumPrice ?? pricing.Middle;
            decimal high = company.HighPrice ?? pricing.High;
            decimal pricePerSms = totalCredits <= 500_000
                ? low
                : totalCredits <= 1_000_000
                    ? mid
                    : high;

            if (company.CreditLimit < totalCredits)
                return BadRequest("Insufficient balance for resending.");

            // 4) Deduct balance & save
            company.CreditLimit -= totalCredits;
            order.ApiId = apiId;
            order.PricePerSms = pricePerSms;
            order.TotalPrice = totalCredits;
            order.CurrentStatus = "WaitingToBeSent";

            _context.BalanceHistory.Add(new BalanceHistory
            {
                CompanyId = company.CompanyId,
                Amount = -totalCredits,
                Action = "Deduct on Resend",
                CreatedAt = TimeHelper.NowInTurkey(),
                CreatedByUserId = userId
            });

            _context.Orders.Update(order);
            _context.Companies.Update(company);
            await _context.SaveChangesAsync();

            // 5) Build request body
            bool isCustom = !string.IsNullOrEmpty(order.PlaceholderColumn);
            var plainNumbers = toSend.Select(r => r.Number).ToArray();

            string requestBody;
            if (isCustom)
            {
                var placeholder = $"{{{order.PlaceholderColumn}}}";
                if (api.ServiceName.Equals("turkcell", StringComparison.OrdinalIgnoreCase))
                {
                    requestBody = JsonConvert.SerializeObject(new
                    {
                        From = api.Originator,
                        User = api.Username,
                        Pass = api.Password,
                        Message = order.MessageText,
                        StartDate = (string?)null,
                        ValidityPeriod = 1440,
                        Messages = toSend.Select(r => new {
                            Message = order.MessageText.Replace(placeholder, r.Name),
                            GSM = r.Number
                        }).ToArray()
                    });
                }
                else
                {
                    requestBody = JsonConvert.SerializeObject(new
                    {
                        Username = api.Username,
                        Password = api.Password,
                        Messages = toSend.Select(r => new {
                            To = r.Number,
                            Text = order.MessageText.Replace(placeholder, r.Name)
                        }).ToArray()
                    });
                }
            }
            else
            {
                // bulk path
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
                else if (api.ContentType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
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

            // 6) Send & log
            using var client = new HttpClient();
            var req = new HttpRequestMessage(HttpMethod.Post, api.ApiUrl)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, api.ContentType)
            };
            var sw = Stopwatch.StartNew();
            var resp = await client.SendAsync(req);
            sw.Stop();
            var respText = await resp.Content.ReadAsStringAsync();

            // sanitize & log
            var sanitized = Regex.Replace(requestBody, @"\d{10,15}", "[NUMBER]");
            _context.ApiCallLogs.Add(new ApiCallLog
            {
                CompanyId = company.CompanyId,
                UserId = userId,
                OrderId = order.OrderId,
                ApiUrl = api.ApiUrl,
                RequestBody = sanitized,
                ResponseContent = respText,
                ResponseTimeMs = sw.ElapsedMilliseconds,
                CreatedAt = TimeHelper.NowInTurkey()
            });
            await _context.SaveChangesAsync();

            // 7) Handle response
            if (!resp.IsSuccessStatusCode)
            {
                // mark failed & refund
                order.CurrentStatus = "Failed";
                order.ApiErrorResponse = $"HTTP {(int)resp.StatusCode} - {respText}";

                company.CreditLimit += totalCredits;
                _context.BalanceHistory.Add(new BalanceHistory
                {
                    CompanyId = company.CompanyId,
                    Amount = totalCredits,
                    Action = "Refund on Failed Resend",
                    CreatedAt = TimeHelper.NowInTurkey(),
                    CreatedByUserId = userId
                });

                _context.Orders.Update(order);
                await _context.SaveChangesAsync();
                return StatusCode((int)resp.StatusCode, $"SMS resend failed: {respText}");
            }

            dynamic json = JsonConvert.DeserializeObject(respText)!;
            if (json.Status == "OK")
            {
                order.SmsOrderId = json.MessageId;
                order.StartedAt = TimeHelper.NowInTurkey();
                order.CompletedAt = TimeHelper.NowInTurkey();
                order.ReportLock = true;
                order.CurrentStatus = "Sent";
                order.Actions.Add(new OrderAction
                {
                    ActionName = "Resent",
                    CreatedAt = TimeHelper.NowInTurkey()
                });

                _context.Orders.Update(order);
                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = _sharedLocalizer["smsresentsuccess"] });
            }
            else
            {
                order.CurrentStatus = "Failed";
                order.ApiErrorResponse = $"Status: {json.Status}, {respText}";
                order.Actions.Add(new OrderAction
                {
                    ActionName = "Sending failed",
                    Message = $"API returned: {json.Status}",
                    CreatedAt = TimeHelper.NowInTurkey()
                });

                _context.Orders.Update(order);
                await _context.SaveChangesAsync();
                return BadRequest($"SMS resend failed: {json.Status}");
            }
        }

        private string BuildRequestBody(SendSmsViewModel model, Api api)
        {
            var rawNumbers = model.PhoneNumbers ?? "";
            var numbersList = rawNumbers
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
            bool isPersonalized =
                 !string.IsNullOrWhiteSpace(model.RecipientsJson)
              && !string.IsNullOrWhiteSpace(model.SelectedCustomColumn)
              && model.Message.Contains($"{{{model.SelectedCustomColumn}}}");
            List<RecipientDto> recs = null;
            if (isPersonalized)
            {
                recs = JsonConvert
                    .DeserializeObject<List<RecipientDto>>(model.RecipientsJson);
            }

            switch (api.ServiceName?.ToLower())
            {
                case "yurtici":
                    if (isPersonalized)
                    {
                        // your “custom” structure:
                        return JsonConvert.SerializeObject(new
                        {
                            Username = api.Username,
                            Password = api.Password,
                            Messages = recs.Select(r => new
                            {
                                To = r.Number,
                                Text = model.Message.Replace(
                                           $"{{{model.SelectedCustomColumn}}}",
                                           r.Name
                                       )
                            }).ToArray()
                        });
                    }
                    // fallback to original flat format
                    return JsonConvert.SerializeObject(new
                    {
                        Username = api.Username,
                        Password = api.Password,
                        Text = model.Message,
                        To = numbersList
                    });

                case "turkcell":
                    if (isPersonalized)
                    {
                        // Turkcell’s custom “Messages” schema:
                        return JsonConvert.SerializeObject(new
                        {
                            From = api.Originator,
                            User = api.Username,
                            Pass = api.Password,
                            Message = model.Message,        // global “subject” if any
                            StartDate = (string)null,
                            ValidityPeriod = 1440,
                            Messages = recs.Select(r => new
                            {
                                Message = model.Message.Replace(
                                              $"{{{model.SelectedCustomColumn}}}",
                                              r.Name
                                          ),
                                GSM = r.Number
                            }).ToArray()
                        });
                    }
                    // fallback flat
                    return JsonConvert.SerializeObject(new
                    {
                        User = api.Username,
                        Pass = api.Password,
                        Message = model.Message,
                        Numbers = numbersList
                    });

                default:
                    // generic JSON fallback
                    if (api.ContentType == "application/json")
                    {
                        if (isPersonalized)
                        {
                            // same pattern if you want to custom‑fall back here, or just do flat
                            return JsonConvert.SerializeObject(new
                            {
                                Username = api.Username,
                                Password = api.Password,
                                Messages = recs.Select(r => new
                                {
                                    To = r.Number,
                                    Text = model.Message.Replace(
                                               $"{{{model.SelectedCustomColumn}}}",
                                               r.Name
                                           )
                                }).ToArray()
                            });
                        }
                        return JsonConvert.SerializeObject(new
                        {
                            Username = api.Username,
                            Password = api.Password,
                            From = api.Originator,
                            Text = model.Message,
                            To = numbersList
                        });
                    }
                    // XML fallback unchanged
                    if (api.ContentType == "text/xml")
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

                company.CreditLimit -= totalSmsCredits;
                order.PricePerSms = pricePerSms;
                order.TotalPrice = totalSmsCredits;

                _context.BalanceHistory.Add(new BalanceHistory
                {
                    CompanyId = company.CompanyId,
                    Amount = (decimal)-totalSmsCredits,
                    Action = "Deduct on Send (Approval)",
                    CreatedAt = TimeHelper.NowInTurkey(),
                    CreatedByUserId = userId
                });


                _context.Orders.Update(order);
                _context.Companies.Update(company);
                await _context.SaveChangesAsync();

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
                    CompanyId = company.CompanyId,
                    UserId = userId,
                    OrderId = order.OrderId,
                    ApiUrl = api.ApiUrl,
                    RequestBody = sanitizedBody,
                    ResponseContent = result,
                    ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                    CreatedAt = TimeHelper.NowInTurkey()
                });
                await _context.SaveChangesAsync();

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
                    var oldNotifs = await _context.Notifications
                    .Where(n =>
                        n.CompanyId == order.CompanyId
                        && n.Type == NotificationType.SmsAwaitingApproval
                        // your description format was "... order #49173 ..." so:
                        && n.Description.Contains($"#{order.OrderId}")
                    )
                    .ToListAsync();
                    foreach (var n in oldNotifs)
                    {
                        n.IsRead = true;
                        _context.Notifications.Update(n);
                    }
                    await _context.SaveChangesAsync();
                    var statusPayload = new
                    {
                        orderId = order.OrderId,
                        newStatus = order.CurrentStatus
                    };
                    await _hubContext.Clients
                   .Group("Admins")
                   .SendAsync("OrderStatusChanged", statusPayload);
                    await _hubContext.Clients
                    .Group("PanelUsers")
                    .SendAsync("OrderStatusChanged", statusPayload);

                    // 2) Company‐wide main user(s)
                    await _hubContext.Clients
                        .Group($"company_{order.CompanyId}")
                        .SendAsync("OrderStatusChanged", statusPayload);

                    // 3) The original creator (sub‐user)
                    await _hubContext.Clients
                        .Group($"user_{order.CreatedByUserId}")
                        .SendAsync("OrderStatusChanged", statusPayload);
                    var notif = new Notifications
                    {
                        Title = _sharedLocalizer["OrderApprovedTitle"],
                        Description = string.Format(_sharedLocalizer["OrderApprovedDesc"], order.OrderId),
                        Type = NotificationType.OrderApproved,
                        CreatedAt = TimeHelper.NowInTurkey(),
                        IsRead = false,
                        CompanyId = order.CompanyId,
                        OrderId = order.OrderId,
                        UserId = order.CreatedByUserId
                    };

                    // 2) Persist it
                    await _notificationService.AddNotificationAsync(notif);

                    // 3) Now EF has populated notif.NotificationId
                    var newNotificationId = notif.NotificationId;

                    // 4) Build your SignalR payload
                    var payload = new
                    {
                        notificationId = newNotificationId,
                        title = notif.Title,
                        description = notif.Description,
                        type = (int)notif.Type,
                        createdAt = notif.CreatedAt,
                        companyId = notif.CompanyId,
                        orderId = notif.OrderId,
                        userId = notif.UserId
                    };

                    // 5) Send it to the creator’s personal group
                    await _hubContext.Clients
                        .Group($"user_{order.CreatedByUserId}")
                        .SendAsync("ReceiveNotification", payload);
              
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

                // Not scheduled, send now
                var api = order.Api;
                var payload = new
                {
                    Username = api.Username,
                    Password = api.Password,
                    Text = order.MessageText,
                    To = numbers
                };

                var contentType = api.ContentType;
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
                else if (contentType == "application/json")
                {
                    requestBody = JsonConvert.SerializeObject(payload);
                }
                else if (contentType == "text/xml")
                {
                    requestBody = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
                    <SendSms>
                      <Username>{api.Username}</Username>
                      <Password>{api.Password}</Password>
                      <From>{api.Originator}</From>
                      <Text>{order.MessageText}</Text>
                      <To>{string.Join(",", numbers)}</To>
                    </SendSms>";
                }
                else
                {
                    return BadRequest("Unsupported API format.");
                }

                using var client = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Post, api.ApiUrl)
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, contentType)
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
   
            
        private string BuildRequestBody(string text, SendSmsViewModel model, Api api)
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
        [HttpPost]
        public async Task<JsonResult> UploadCustomRecipients(
     IFormFile file,
     string nameColumn,
     string numberColumn
 )
        {
            if (file == null
             || string.IsNullOrEmpty(nameColumn)
             || string.IsNullOrEmpty(numberColumn))
            {
                // still return an empty array for `records`
                return Json(new { records = Array.Empty<object>() });
            }

            // ParseRecipientsFromFile pulls out both Name and Number
            var recs = ParseRecipientsFromFile(
                file: file,
                hasName: true,
                nameColumn: nameColumn,
                numberColumn: numberColumn
            );

            // Project exactly into Name/Number tuples
            var payload = recs
                .Select(r => new { Name = r.Name, Number = r.Number })
                .ToArray();

            // 🎯 Return a flat array under `records`
            return Json(new { records = payload });
        }
        [HttpPost]
        public async Task<IActionResult> UploadNumbers(List<IFormFile> files)
        {
            // fallback if JS bound it under a different name
            if (files == null || files.Count == 0)
                files = Request.Form.Files.ToList();

            if (files.Count == 0)
                return Json(new { numbers = Array.Empty<string>() });

            var phoneNumbers = new List<string>();
            foreach (var file in files)
            {
                if (file.Length == 0) continue;
                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                stream.Position = 0;

                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (ext == ".csv" || ext == ".txt")
                {
                    using var reader = new StreamReader(stream);
                    while (!reader.EndOfStream)
                    {
                        var line = await reader.ReadLineAsync();
                        if (!string.IsNullOrWhiteSpace(line))
                            phoneNumbers.Add(line.Trim());
                    }
                }
                else if (ext == ".xlsx")
                {
                    using var wb = new XLWorkbook(stream);
                    var ws = wb.Worksheet(1);
                    foreach (var row in ws.RowsUsed())
                    {
                        var cell = row.Cell(1).GetString();
                        if (!string.IsNullOrWhiteSpace(cell))
                            phoneNumbers.Add(cell.Trim());
                    }
                }
            }

            return Json(new { numbers = phoneNumbers });
        }

        [HttpGet]
        public async Task<IActionResult> GetAllOrders(string status = null)
        {
            var userType = HttpContext.Session.GetString("UserType") ?? "";
            var companyId = HttpContext.Session.GetInt32("CompanyId");
            var roleId = HttpContext.Session.GetInt32("RoleId");
            var isAdmin = roleId == 1;
            var ordersQuery = _context.Orders
                .Include(o => o.Company)
                .Include(o => o.Api)
                .Include(o => o.CreatedByUser)
                .AsQueryable();

            // ✅ Filter by company if it's a CompanyUser
            if (userType.Equals("CompanyUser", StringComparison.OrdinalIgnoreCase) && companyId.HasValue)
            {
                ordersQuery = ordersQuery.Where(o => o.CompanyId == companyId.Value);
            }

            // ✅ Apply status filter if provided
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
                    Status = o.CurrentStatus == "AwaitingApproval" || o.CurrentStatus == "Awaiting approval" ? "AwaitingApproval" :
                             o.CurrentStatus == "Waiting to be sent" || o.CurrentStatus == "WaitingToBeSent" ? "WaitingToBeSent" :
                             o.CurrentStatus == "Shipping has started" ? "Shipping has started" :
                             o.CurrentStatus == "Sent" ? "Sent" :
                             o.CurrentStatus == "Failed" || o.CurrentStatus == "Sending failed" || o.CurrentStatus == "Submission failed" ? "Failed" :
                             o.CurrentStatus == "Cancelled" ? "Cancelled" :
                             o.CurrentStatus,
                    CompanyName = isAdmin ? (o.Company != null ? o.Company.CompanyName : null) : null,
                    ApiName = isAdmin ? (o.Api != null ? o.Api.ServiceName : null) : null,
                    SubmissionType = isAdmin ? o.SubmissionType : null,
                    LoadedCount = isAdmin ? o.LoadedCount : (int?)null,
                    o.ProcessedCount,
                    UnsuccessfulCount = isAdmin ? o.UnsuccessfulCount : (int?)null,
                    CreatedBy = o.CreatedByUser != null ? o.CreatedByUser.FullName : null,
                    DateOfSending = o.ScheduledSendDate,
                    Refundable = isAdmin ? o.Refundable : (bool?)null,
                    o.Returned,
                    ReturnDate = isAdmin ? o.ReturnDate : (DateTime?)null,
                    o.CreatedAt
                })
                .ToListAsync();

            return Json(orders);
        }
        [HttpPost]
        public async Task<IActionResult> CancelOrder(int orderId)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order == null)
            {
                return NotFound(new { messageKey = "OrderNotFound" });
            }

            if (order.CurrentStatus == "Sent" || order.CurrentStatus == "Cancelled")
            {
                return BadRequest(new { messageKey = "OrderCannotBeCancelled" });
            }

            order.CurrentStatus = "Cancelled";
            order.UpdatedAt = TimeHelper.NowInTurkey();
            order.Actions.Add(new OrderAction
            {
                ActionName = "Cancelled",
                Message = "OrderWasCancelled", // Store key or localized text as you prefer
                CreatedAt = TimeHelper.NowInTurkey()
            });

            _context.Orders.Update(order);
            await _context.SaveChangesAsync();

            return Ok(new { messageKey = "OrderCancelledSuccessfully" });
        }
        public IActionResult GetOrderDetails(int id)
        {

            var order = _context.Orders
                .Include(o => o.Company)
                .Include(o => o.Api)
                .Include(o => o.CreatedByUser)
                .Include(o => o.Actions.OrderBy(a => a.CreatedAt))
                .FirstOrDefault(o => o.OrderId == id);

            if (order == null)
            {
                return NotFound();
            }

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

            return PartialView("_OrderDetailsPartial", order);// 🚀 THIS IS CORRECT
        }

        [HttpPost]
        public IActionResult GetHeaderColumns(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var headers = new List<string>();

            if (ext is ".csv" or ".txt")
            {
                using var reader = new StreamReader(file.OpenReadStream());
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                csv.Read();
                csv.ReadHeader();
                headers = csv.HeaderRecord.ToList();
            }
            else if (ext is ".xls" or ".xlsx")
            {
                using var stream = file.OpenReadStream();
                using var wb = new XLWorkbook(stream);
                var ws = wb.Worksheet(1);

                headers = ws
                  .Row(1)
                  .CellsUsed()
                  .Select(c => c.GetString())
                  .ToList();
            }
            else
            {
                return BadRequest("Unsupported file type");
            }

            return Json(headers);
        }
        private List<(string Name, string Number)> ParseRecipientsFromFile(
      IFormFile file,
      bool hasName,
      string nameColumn,
      string numberColumn
  )
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

            // 1) Peek at row1 & row2
            string[] firstRow, secondRow = null;
            bool hasSecond = false;
            if (ext is ".csv" or ".txt")
            {
                using var peek = new StreamReader(file.OpenReadStream(), leaveOpen: true);
                using var csv = new CsvReader(peek, CultureInfo.InvariantCulture);
                if (!csv.Read()) throw new InvalidOperationException("Empty file");
                firstRow = csv.Parser.Record;
                hasSecond = csv.Read();
                if (hasSecond) secondRow = csv.Parser.Record;
                peek.BaseStream.Position = 0;
            }
            else if (ext is ".xls" or ".xlsx")
            {
                using var ms = new MemoryStream();
                file.CopyTo(ms);
                ms.Position = 0;
                using var wb = new XLWorkbook(ms);
                var ws = wb.Worksheet(1);
                firstRow = ws.Row(1).CellsUsed().Select(c => c.GetString()).ToArray();
                var last = ws.LastRowUsed().RowNumber();
                if (last >= 2)
                {
                    hasSecond = true;
                    secondRow = ws.Row(2)
                                   .Cells(1, firstRow.Length)
                                   .Select(c => c.GetString())
                                   .ToArray();
                }
            }
            else throw new NotSupportedException("Unsupported file type");

            // 2) Decide if row1 is a real header
            bool row1HasLetters = firstRow.Any(f => Regex.IsMatch(f, "[A-Za-z]"));
            bool row2AllNumeric = hasSecond
                && secondRow.All(s => Regex.IsMatch(s, @"^[\d\+\-\s\(\)]+$"));
            bool headerExists = row1HasLetters && row2AllNumeric;

            // 3) Build the column list
            var columns = headerExists
                ? firstRow.ToList()
                : firstRow.Select((_, i) => $"Column_{IndexToLetters(i)}").ToList();

            // 4) Find indexes for name & number
            int nameIdx = hasName ? columns.IndexOf(nameColumn) : -1;
            int numberIdx = columns.IndexOf(numberColumn);

            if (numberIdx < 0)
                throw new ArgumentException($"Number column '{numberColumn}' not found.");
            if (hasName && nameIdx < 0)
                throw new ArgumentException($"Name column '{nameColumn}' not found.");

            // 5) Re‑read the file and extract all rows
            var results = new List<(string, string)>();
            if (ext is ".csv" or ".txt")
            {
                using var reader = new StreamReader(file.OpenReadStream());
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                if (headerExists)
                {
                    csv.Read(); csv.ReadHeader();
                }
                while (csv.Read())
                {
                    var name = hasName ? csv.GetField(nameIdx) : "";
                    var number = csv.GetField(numberIdx);
                    results.Add((name, number));
                }
            }
            else
            {
                using var stream = file.OpenReadStream();
                using var wb = new XLWorkbook(stream);
                var ws = wb.Worksheet(1);
                int start = headerExists ? 2 : 1;
                for (int r = start; r <= ws.LastRowUsed().RowNumber(); r++)
                {
                    var row = ws.Row(r);
                    var name = hasName ? row.Cell(nameIdx + 1).GetString() : "";
                    var number = row.Cell(numberIdx + 1).GetString();
                    results.Add((name, number));
                }
            }

            return results;
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.Orders
                .Include(o => o.Company)
                .Include(o => o.Api)
                .Include(o => o.CreatedByUser)
                .Include(o => o.Actions.OrderBy(a => a.CreatedAt))
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
            {
                return NotFound();
            }

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

            return View(order);// 🚀 FULL PAGE
        }
        [HttpGet]
        public async Task<IActionResult> GlobalSearch(string term)
        {
            var lowerTerm = term?.ToLower() ?? "";
        
            var currentUser = await _userManager.GetUserAsync(User);
            var user = await _context.Users
            .Include(u => u.Company)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .ThenInclude(r => r.RolePermissions)
            .FirstOrDefaultAsync(u => u.Id == currentUser.Id);
            if (user != null)
            {
                SessionHelper.SetUserSession(HttpContext.Session, user); 
            }

            var permissions = HttpContext.Session.GetPermissions(); // or query from DB
            int? companyId = currentUser.CompanyId;
            bool isCompanyUser = currentUser.UserType == "CompanyUser";
            var isMainUser = HttpContext.Session.GetInt32("IsMainUser") == 1;
            bool HasRead(string module) =>
                permissions.Any(p => p.Module == module && p.CanRead);

            // Static Pages (only show if user has access)
            var pageResults = new List<SearchResultDto>();

            if (HasRead("Order"))
                pageResults.Add(new SearchResultDto { Type = "Page", Name = "Orders", Url = "/Orders" });

            if (HasRead("Firm"))
                pageResults.Add(new SearchResultDto { Type = "Page", Name = "Companies", Url = "/Companies" });

            if (HasRead("Company_User") || HasRead("User"))
                pageResults.Add(new SearchResultDto { Type = "Page", Name = "Users", Url = "/Users" });

            pageResults = pageResults
                .Where(p => p.Name.ToLower().Contains(lowerTerm))
                .ToList();

            // Companies
            var companyResults = new List<SearchResultDto>();
            if (HasRead("Firm"))
            {
                var companyQuery = _context.Companies.AsQueryable();
                if (isCompanyUser && companyId.HasValue)
                    companyQuery = companyQuery.Where(c => c.CompanyId == companyId);

                companyResults = await companyQuery
                    .Where(c => c.CompanyName.Contains(term))
                    .Select(c => new SearchResultDto
                    {
                        Type = "Company",
                        Name = c.CompanyName,
                        Url = "/Companies/Details/" + c.CompanyId
                    })
                    .ToListAsync();
            }

            // Users
            var userResults = new List<SearchResultDto>();
            if (HasRead("Company_User") || HasRead("User"))
            {
                var userQuery = _context.Users.AsQueryable();

                if (isCompanyUser && companyId.HasValue)
                {
                    userQuery = userQuery.Where(u => u.CompanyId == companyId);
                    if (!isMainUser)
                        userQuery = userQuery.Where(u => u.CreatedByUserId == currentUser.Id);
                }

                userResults = await userQuery
                    .Where(u => u.FullName.Contains(term))
                    .Select(u => new SearchResultDto
                    {
                        Type = "User",
                        Name = u.FullName,
                        Url = "/Users/Details/" + u.Id
                    })
                    .ToListAsync();
            }

            // Orders
            var orderResults = new List<SearchResultDto>();
            if (HasRead("Order") && int.TryParse(term, out int orderId))
            {
                var orderQuery = _context.Orders.AsQueryable();

                if (isCompanyUser && companyId.HasValue)
                {
                    orderQuery = orderQuery.Where(o => o.CompanyId == companyId);
                    if (!isMainUser)
                        orderQuery = orderQuery.Where(o => o.CreatedByUserId == currentUser.Id);
                }

                var order = await orderQuery.FirstOrDefaultAsync(o => o.OrderId == orderId);
                if (order != null)
                {
                    orderResults.Add(new SearchResultDto
                    {
                        Type = "Order",
                        Name = $"Order #{order.OrderId}",
                        Url = "/Home/Details/" + order.OrderId
                    });
                }
            }

            var results = pageResults
                .Concat(companyResults)
                .Concat(userResults)
                .Concat(orderResults)
                .ToList();

            return Json(results);
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
        public IActionResult DownloadReportFile(int orderId, string fileName, string reportName)
        {
            // 1️⃣ Find the Kudu/App_Data path
            var home = Environment.GetEnvironmentVariable("HOME")
                             ?? _env.ContentRootPath;
            var ordersRoot = Path.Combine(home, "data", "orders");
            var folder = Path.Combine(ordersRoot, orderId.ToString());

            // 2️⃣ Build both possible source paths (CSV or TXT)
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var csvPath = Path.Combine(folder, baseName + ".csv");
            var txtPath = Path.Combine(folder, baseName + ".txt");

            // 3️⃣ Pick whichever exists
            string source;
            if (System.IO.File.Exists(csvPath))
                source = csvPath;
            else if (System.IO.File.Exists(txtPath))
                source = txtPath;
            else
                return NotFound($"Neither '{baseName}.csv' nor '{baseName}.txt' was found for order {orderId}.");

            // 4️⃣ Final download‑as name
            var downloadName = reportName ?? fileName;

            // 5️⃣ Branch on requested extension
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            switch (ext)
            {
                case ".csv":
                    {
                        var data = System.IO.File.ReadAllBytes(source);
                        return File(data, "text/csv", downloadName);
                    }
                case ".txt":
                    {
                        var data = System.IO.File.ReadAllBytes(source);
                        return File(data, "text/plain", downloadName);
                    }
                case ".xlsx":
                    {
                        // Build an in‑memory XLSX from CSV/TXT
                        using var wb = new XLWorkbook();
                        var ws = wb.Worksheets.Add("Report");
                        var lines = System.IO.File.ReadAllLines(source);

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



        [HttpGet]
        public async Task<IActionResult> GetTodaySmsStats()
        {
            var today = TimeHelper.NowInTurkey().Date;
            var tomorrow = today.AddDays(1);
            var yesterday = today.AddDays(-1);

            var companyId = HttpContext.Session.GetInt32("CompanyId");
            var isCompanyUser = HttpContext.Session.GetString("UserType")?.Equals("CompanyUser", StringComparison.OrdinalIgnoreCase) == true;

            var baseQuery = _context.Orders
                .Where(o => o.CurrentStatus == "Sent" && o.ScheduledSendDate.HasValue);

            if (isCompanyUser && companyId.HasValue)
            {
                baseQuery = baseQuery.Where(o => o.CompanyId == companyId.Value);
            }

            var sentTodayCount = await baseQuery
                .Where(o => o.ScheduledSendDate >= today && o.ScheduledSendDate < tomorrow)
                .CountAsync();

            var sentYesterdayCount = await baseQuery
                .Where(o => o.ScheduledSendDate >= yesterday && o.ScheduledSendDate < today)
                .CountAsync();

            var dailyQuota = 10000;
            var progressPercent = (int)(sentTodayCount * 100.0 / dailyQuota);
            if (progressPercent > 100) progressPercent = 100;

            return Json(new
            {
                todayCount = sentTodayCount,
                yesterdayCount = sentYesterdayCount,
                progressPercent
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetMonthlySmsStats()
        {
            var companyId = HttpContext.Session.GetInt32("CompanyId");
            var isCompanyUser = HttpContext.Session.GetString("UserType")?.Equals("CompanyUser", StringComparison.OrdinalIgnoreCase) == true;

            var now = TimeHelper.NowInTurkey();
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var startOfPrevMonth = startOfMonth.AddMonths(-1);
            var endOfPrevMonth = startOfMonth.AddDays(-1);
            var endOfMonth = now;

            var baseQuery = _context.Orders.Where(o => o.CurrentStatus == "Sent");

            if (isCompanyUser && companyId.HasValue)
                baseQuery = baseQuery.Where(o => o.CompanyId == companyId.Value);

            // Current month
            var currentMonthData = await baseQuery
                .Where(o => o.CreatedAt >= startOfMonth && o.CreatedAt <= endOfMonth)
                .GroupBy(o => o.CreatedAt.Day)
                .Select(g => new { day = g.Key, count = g.Sum(o => o.ProcessedCount) })
                .ToListAsync();

            // Previous month
            var prevMonthData = await baseQuery
                .Where(o => o.CreatedAt >= startOfPrevMonth && o.CreatedAt <= endOfPrevMonth)
                .GroupBy(o => o.CreatedAt.Day)
                .Select(g => new { day = g.Key, count = g.Sum(o => o.ProcessedCount) })
                .ToListAsync();

            return Json(new
            {
                currentMonth = currentMonthData,
                previousMonth = prevMonthData
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetDashboardStats()
        {
            var companyId = HttpContext.Session.GetInt32("CompanyId");
            var isCompanyUser = HttpContext.Session.GetString("UserType")?.Equals("CompanyUser", StringComparison.OrdinalIgnoreCase) == true;
            var orderFilter = _context.Orders.AsQueryable();
            var balanceFilter = _context.BalanceHistory.AsQueryable();

            if (isCompanyUser && companyId.HasValue)
            {
                orderFilter = orderFilter.Where(o => o.CompanyId == companyId.Value);
                balanceFilter = balanceFilter.Where(b => b.CompanyId == companyId.Value);
            }
            var totalCount = await orderFilter.SumAsync(o => o.LoadedCount);

            var deliveredCount = await orderFilter
                .Where(o => o.CurrentStatus == "Sent")
                .SumAsync(o => o.ProcessedCount);

            var undeliverableCount = await orderFilter
                .Where(o => o.CurrentStatus == "Failed" || o.CurrentStatus == "Sending failed")
                .SumAsync(o => o.UnsuccessfulCount);

            var onHoldCount = await orderFilter
                .Where(o => o.CurrentStatus == "AwaitingApproval")
                .SumAsync(o => o.LoadedCount);

            var deliveryExpiredCount = await orderFilter
                .Where(o => o.CurrentStatus == "Delivery Expired")
                .SumAsync(o => o.ProcessedCount);

            var refundAmount = await balanceFilter
                .Where(b => b.Action == "Refund on Failed")
                .SumAsync(b => b.Amount);

            return Json(new
            {
                totalCount,
                deliveredCount,
                undeliverableCount,
                onHoldCount,
                deliveryExpiredCount,
                refundAmount = Math.Abs(refundAmount)
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetMonthlySmsVolume()
        {
            var companyId = HttpContext.Session.GetInt32("CompanyId");
            var isCompanyUser = HttpContext.Session.GetString("UserType")?.Equals("CompanyUser", StringComparison.OrdinalIgnoreCase) == true;

            var query = _context.Orders
                .Where(o => o.CurrentStatus == "Sent" && o.CompletedAt != null);

            if (isCompanyUser && companyId.HasValue)
            {
                query = query.Where(o => o.CompanyId == companyId.Value);
            }

            var data = await query
                .GroupBy(o => new
                {
                    o.CompletedAt.Value.Year,
                    o.CompletedAt.Value.Month
                })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    SmsCount = g.Sum(o => o.ProcessedCount)
                })
                .OrderBy(g => g.Year)
                .ThenBy(g => g.Month)
                .ToListAsync();

            return Json(data);
        }
        [HttpGet]
        public IActionResult LoadSendSmsModal()
        {
            var companies = _context.Companies.Where(c => c.IsActive).ToList();
            var apis = _context.Apis.Where(a => a.IsActive && !a.IsClientApi).ToList();

            var firstCompany = companies.FirstOrDefault();
            var defaultApiId = apis.FirstOrDefault(a => a.IsDefault)?.ApiId;

            var model = new HomeIndexViewModel
            {
                Companies = companies,
                ApiLists = apis.Select(a => new SelectListItem
                {
                    Value = a.ApiId.ToString(),
                    Text = a.ServiceName
                }).ToList(),
                DefaultApiId = defaultApiId,
                LowPrice = firstCompany?.LowPrice ?? 0,
                MediumPrice = firstCompany?.MediumPrice ?? 0,
                HighPrice = firstCompany?.HighPrice ?? 0,
                Company = firstCompany
            };

            return PartialView("_SendSmsPartial", model);
        }
        [HttpGet]
        public async Task<IActionResult> GetByCompany(int companyId)
        {
            var userType = HttpContext.Session.GetString("UserType") ?? "";
            var sessionCompanyId = HttpContext.Session.GetInt32("CompanyId");
            var userId = HttpContext.Session.GetInt32("UserId");
            var isMainUser = HttpContext.Session.GetInt32("IsMainUser") == 1;
            var roleId = HttpContext.Session.GetInt32("RoleId");

            //if (roleId != 1)
            //{
            //    if (!sessionCompanyId.HasValue || sessionCompanyId.Value != companyId)
            //    {
            //        return Json(new { success = false, message = "Unauthorized" });
            //    }
            //}

            var query = _context.Directories
                .Where(d => d.CompanyId == companyId);

            if (userType == "CompanyUser" && !isMainUser)
            {
                query = query.Where(d => d.CreatedByUserId == userId);
            }

            var directories = await query
                .Select(d => new {
                    d.DirectoryId,
                    d.DirectoryName
                })
                .ToListAsync();

            return Json(new { success = true, data = directories });
        }
        [HttpGet]
        public IActionResult GetDirectoryNumbers(int directoryId)
        {
            var directory = _context.Directories
                .Include(d => d.DirectoryNumbers)
                .FirstOrDefault(d => d.DirectoryId == directoryId);

            if (directory == null)
                return Json(new { success = false, message = "Directory not found." });

            var numbers = directory.DirectoryNumbers.Select(n => n.PhoneNumber).ToList();

            return Json(new { success = true, numbers });
        }
        [HttpGet]
        public async Task<IActionResult> GetCompanyPrices(int companyId)
        {
            var company = await _context.Companies.FindAsync(companyId);

            if (company == null)
                return NotFound();

            // Use company's selected API if available
            int? selectedApiId = company.Apid;

            // If null, fallback to default API from Apis table
            if (selectedApiId == null)
            {
                selectedApiId = await _context.Apis
                    .Where(a => a.IsDefault) // or your flag
                    .Select(a => (int?)a.ApiId)
                    .FirstOrDefaultAsync();
            }

            return Json(new
            {
                low = company.LowPrice,
                medium = company.MediumPrice,
                high = company.HighPrice,
                selectedApiId = selectedApiId
            });
        }
    }
}
