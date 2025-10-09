using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using GittBilSmsCore.Helpers;
using Microsoft.Extensions.Localization;

namespace GittBilSmsCore.Controllers
{
    public class CreditController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly IStringLocalizer _sharedLocalizer;
        public CreditController(GittBilSmsDbContext context, IStringLocalizerFactory factory) : base(context)
        {
            _context = context;
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
        }
        public IActionResult Index()
        {
            return View();
        }
        [HttpGet]
        public async Task<IActionResult> GetTopups()
        {
            // Step 1: Get the latest transaction ID per company for "Credit added"
            var latestTransactionIds = await _context.CreditTransactions
               .Where(x => x.TransactionType == "Credit added" || x.TransactionType == "Kredi eklendi")
                .GroupBy(x => x.CompanyId)
                .Select(g => g.OrderByDescending(x => x.TransactionDate).Select(x => x.CreditTransactionId).FirstOrDefault())
                .ToListAsync();

            // Step 2: Get the full data for those transactions
            var data = await _context.CreditTransactions
                .Where(x => latestTransactionIds.Contains(x.CreditTransactionId))
                .Select(x => new
                {
                    companyId = x.CompanyId,
                    companyName = x.Company.CompanyName,
                    credit = x.Credit,
                    totalPrice = x.TotalPrice,
                    unitPrice = x.UnitPrice,
                    currency = x.Currency,
                    transactionDate = x.TransactionDate
                })
                .OrderByDescending(x => x.transactionDate)
                .ToListAsync();

            return Json(data);
        }
    }
}
