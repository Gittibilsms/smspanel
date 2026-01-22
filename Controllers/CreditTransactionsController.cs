using DocumentFormat.OpenXml.ExtendedProperties;
using GittBilSmsCore.Data;
using GittBilSmsCore.Helpers;
using GittBilSmsCore.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
namespace GittBilSmsCore.Controllers
{
    public class CreditTransactionsController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly IStringLocalizer _sharedLocalizer;
        private readonly TelegramMessageService _svc;
        public CreditTransactionsController(GittBilSmsDbContext context, IStringLocalizerFactory factory, TelegramMessageService svc) : base(context)
        {
            _context = context;
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
            _svc = svc;
        }

        // GET: /CreditTransactions?companyId=123
        public async Task<IActionResult> Index(int companyId)
        {
            var transactions = await _context.CreditTransactions
                .Where(t => t.CompanyId == companyId)
                .OrderByDescending(t => t.TransactionDate)
                .ToListAsync();

            ViewBag.CompanyId = companyId;
            return View(transactions);
        }

        [HttpGet]
        public async Task<IActionResult> GetTransactions(int companyId)
        {
            var transactions = await _context.CreditTransactions
                .Where(t => t.CompanyId == companyId)
                .OrderByDescending(t => t.TransactionDate)
                .Select(t => new {
                    t.TransactionType,
                    Credit = t.Credit.ToString("N2"),
                    Total = "₺" + t.TotalPrice.ToString("N0"),
                    t.Currency,
                    t.UnitPrice,
                    TransactionDate = TimeHelper.ToTurkeyTime(t.TransactionDate).ToString("dd/MM/yyyy HH:mm"),
                    Note = t.Note ?? ""
                })
                .ToListAsync();

            return Json(new { data = transactions });
        }
   
        [HttpPost]
        public async Task<IActionResult> AddCredit(int companyId, decimal price, decimal unitPrice, decimal credit, string currency, string note)
        {
            try
            {
                var company = await _context.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId);
                if (company == null)
                    return Json(new { success = false, message = "Company not found." });

                var transaction = new CreditTransaction
                {
                    CompanyId = companyId,
                    TransactionType = _sharedLocalizer["creditadded"],
                    Credit = credit,
                    TotalPrice = price,
                    UnitPrice = unitPrice,
                    Currency = currency,
                    Note = note,
                    TransactionDate = TimeHelper.NowInTurkey()
                };

                _context.CreditTransactions.Add(transaction);
                company.CreditLimit += credit;
                await _context.SaveChangesAsync();
                int performedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
                decimal? availableCredit = await (
                        from c in _context.Companies
                        join u in _context.Users on c.CompanyId equals u.CompanyId
                        where u.IsMainUser == true && c.CompanyId == companyId
                        select (decimal?)c.CreditLimit
                    ).FirstOrDefaultAsync();

                var textMsg = string.Format(
                                      _sharedLocalizer["Creditaddedmessage"],
                                      credit,
                                      availableCredit
                                  );
                string ? companyName = await (
                        from c in _context.Companies
                        join u in _context.Users on c.CompanyId equals u.CompanyId
                        where u.IsMainUser == true && c.CompanyId == companyId
                        select (string?)c.CompanyName
                    ).FirstOrDefaultAsync();
                var textMsgtoAdmin = string.Format(
                                           _sharedLocalizer["CreditaddedmessagetoAdmin"],
                                           companyName,
                                           credit,
                                           availableCredit
                                       );
                var userName = _context.Users.Find(performedByUserId)?.UserName ?? "UnknownUser";
                string dataJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Message = "Credit Added by : " + userName,
                    TelegramMessage = textMsg,
                    Time = TimeHelper.NowInTurkey(),
                    IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = Request.Headers["User-Agent"].ToString()
                });
                await _svc.SendToUsersAsync(companyId, performedByUserId, textMsg, dataJson, textMsgtoAdmin, 0);

                return Json(new
                {
                    success = true,
                    message = _sharedLocalizer["Credit_Added_Successfully"]
                });
            }
            catch (Exception ex)
            {
                // Log the exception (optional)
                return Json(new
                {
                    success = false,
                    message = _sharedLocalizer["Error_Adding_Credit"],
                    error = ex.Message
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteCredit(int companyId, decimal credit, string currency, string note)
        {
            var company = await _context.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId);
            if (company == null)
                return Json(new { success = false, message = "Company not found." });

            if (company.CreditLimit < credit)
                return Json(new { success = false, message = _sharedLocalizer["notenoughcredit"] });

            var transaction = new CreditTransaction
            {
                CompanyId = companyId,
                TransactionType = _sharedLocalizer["creditdeleted"],
                Credit = -credit,
                TotalPrice = 0,
                UnitPrice = 0,
                Currency = currency,
                Note = note ?? string.Empty,
                TransactionDate = TimeHelper.NowInTurkey()
            };

            _context.CreditTransactions.Add(transaction);

            company.CreditLimit -= credit;

            await _context.SaveChangesAsync();

            decimal? availableCredit = await (
                         from c in _context.Companies
                         join u in _context.Users on c.CompanyId equals u.CompanyId
                         where u.IsMainUser == true && c.CompanyId == companyId
                         select (decimal?)c.CreditLimit
                     ).FirstOrDefaultAsync();
            int performedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
            var textMsg = string.Format(
                                        _sharedLocalizer["Creditdeletedmessage"],
                                        credit,
                                        availableCredit
                                    );
            string? companyName = await (
                        from c in _context.Companies
                        join u in _context.Users on c.CompanyId equals u.CompanyId
                        where u.IsMainUser == true && c.CompanyId == companyId
                        select (string?)c.CompanyName
                    ).FirstOrDefaultAsync();
            var textMsgtoAdmin = string.Format(
                                       _sharedLocalizer["CreditdeletedmessagetoAdmin"],
                                       companyName,
                                       credit,
                                       availableCredit
                                   );
            var userName = _context.Users.Find(performedByUserId)?.UserName ?? "UnknownUser";
            string dataJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                Message = "Credit Deleted by : " + userName,
                TelegramMessage = textMsg,
                Time = TimeHelper.NowInTurkey(),
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString()
            });

            await _svc.SendToUsersAsync(companyId, performedByUserId, textMsg, dataJson, textMsgtoAdmin, 0);
            return Json(new
            {
                success = true,
                message = _sharedLocalizer["creditdeletedsuccess"],
                newCredit = company.CreditLimit
            });
        }
    }
    }
