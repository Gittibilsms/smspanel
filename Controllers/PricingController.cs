using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using GittBilSmsCore.Helpers;
using System.Globalization;
namespace GittBilSmsCore.Controllers
{
    public class PricingController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private static readonly string[] AllowedCurrencies = { "TRY" };
        private const decimal MinUnitPrice = 0.0001m;
        private const decimal MaxUnitPrice = 10m;
        public PricingController(GittBilSmsDbContext context) : base(context) 
        {
            _context = context;
        }

        // GET: /Pricing
        public async Task<IActionResult> Index()
        {
            var roleId = HttpContext.Session.GetInt32("RoleId");

            if (roleId != 1) // 1 = Admin
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            // Get latest pricing for the form
            var latestPricing = await _context.Pricing
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            ViewBag.LatestPricing = latestPricing;

            // Get full pricing history
            var pricingHistory = await _context.Pricing
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            ViewBag.PricingHistory = pricingHistory;

            return View();
        }

        // POST: /Pricing/Update

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(string currency, string low, string middle, string high, string action)
        {
            // Auth guard
            var roleId = HttpContext.Session.GetInt32("RoleId");
            if (roleId != 1)
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            // ── Remove branch ─────────────────────────────────────────────
            if (action == "remove")
            {
                var latestActive = await _context.Pricing
                    .Where(p => p.IsActive)
                    .OrderByDescending(p => p.CreatedAt)
                    .FirstOrDefaultAsync();

                if (latestActive != null)
                {
                    latestActive.IsActive = false;
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Special pricing removed successfully.";
                }
                else
                {
                    TempData["ErrorMessage"] = "No active pricing to remove.";
                }
                return RedirectToAction(nameof(Index));
            }
          

           
            if (string.IsNullOrWhiteSpace(currency) || !AllowedCurrencies.Contains(currency))
            {
                TempData["ErrorMessage"] = "Please select a valid currency.";
                return RedirectToAction(nameof(Index));
            }

          
            if (string.IsNullOrWhiteSpace(low) ||
                string.IsNullOrWhiteSpace(middle) ||
                string.IsNullOrWhiteSpace(high))
            {
                TempData["ErrorMessage"] = "Low, Medium and High prices are all required.";
                return RedirectToAction(nameof(Index));
            }

          
            if (!decimal.TryParse(low, NumberStyles.Number, CultureInfo.InvariantCulture, out var lowValue) ||
                !decimal.TryParse(middle, NumberStyles.Number, CultureInfo.InvariantCulture, out var middleValue) ||
                !decimal.TryParse(high, NumberStyles.Number, CultureInfo.InvariantCulture, out var highValue))
            {
                TempData["ErrorMessage"] = "Prices must be valid numbers (e.g. 0.23).";
                return RedirectToAction(nameof(Index));
            }
            
            if (lowValue < MinUnitPrice || lowValue > MaxUnitPrice ||
                middleValue < MinUnitPrice || middleValue > MaxUnitPrice ||
                highValue < MinUnitPrice || highValue > MaxUnitPrice)
            {
                TempData["ErrorMessage"] = $"Each price must be between {MinUnitPrice} and {MaxUnitPrice}.";
                return RedirectToAction(nameof(Index));
            }
           
            if (!(lowValue >= middleValue && middleValue >= highValue))
            {
                TempData["ErrorMessage"] =
                    "Tier order invalid: Low ≥ Medium ≥ High (higher volume must get a cheaper unit price).";
                return RedirectToAction(nameof(Index));
            }

           
            var currentActive = await _context.Pricing
                .Where(p => p.IsActive)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            if (currentActive != null
                && currentActive.Currency == currency
                && currentActive.Low == lowValue
                && currentActive.Middle == middleValue
                && currentActive.High == highValue)
            {
                TempData["ErrorMessage"] = "No changes — submitted pricing matches the current active pricing.";
                return RedirectToAction(nameof(Index));
            }
            
            var activePricings = await _context.Pricing.Where(p => p.IsActive).ToListAsync();
            foreach (var p in activePricings)
            {
                p.IsActive = false;
            }

            _context.Pricing.Add(new Pricing
            {
                Currency = currency,
                Low = lowValue,
                Middle = middleValue,
                High = highValue,
                CreatedAt = TimeHelper.NowInTurkey(),
                IsActive = true
            });

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Pricing updated successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}
