using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using GittBilSmsCore.Helpers;
using Microsoft.Extensions.Localization;
namespace GittBilSmsCore.Controllers
{
    public class CreditTransactionsController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly IStringLocalizer _sharedLocalizer;
        public CreditTransactionsController(GittBilSmsDbContext context, IStringLocalizerFactory factory) : base(context)
        {
            _context = context;
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
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
                await _context.SaveChangesAsync();

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

            // ✅ Return updated values
            return Json(new
            {
                success = true,
                message = _sharedLocalizer["creditdeletedsuccess"],
                newCredit = company.CreditLimit
            });
        }
    }
    }
