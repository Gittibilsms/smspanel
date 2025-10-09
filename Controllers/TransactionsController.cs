using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.Json;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GittBilSmsCore.Controllers
{
    public class TransactionsController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly UserManager<User> _userManager;

        public TransactionsController(GittBilSmsDbContext context,  UserManager<User> userManager) : base(context)
        {
            _context = context;
            _userManager = userManager;
        }
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetCompanyTransactions()
        {
            var companyId = HttpContext.Session.GetInt32("CompanyId");
            if (companyId == null)
                return new JsonResult(new { data = new List<object>() });

            var transactions = _context.CreditTransactions
                .Where(t => t.CompanyId == companyId)
                .OrderByDescending(t => t.TransactionDate)
                .AsEnumerable()
                .Select(t => new
                {
                    transactionType = t.TransactionType,
                    credit = t.Credit,
                    totalPrice = t.TotalPrice,
                    currency = t.Currency,
                    unitPrice = t.UnitPrice,
                    transactionDate = t.TransactionDate.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
                })
                .ToList();

            // ✅ This disables $id and $values
            var jsonOptions = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

            return new JsonResult(new { data = transactions }, jsonOptions);
        }
    }
}
