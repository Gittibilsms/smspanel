using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using GittBilSmsCore.Helpers;
namespace GittBilSmsCore.Controllers
{
    public class PricingController : BaseController
    {
        private readonly GittBilSmsDbContext _context;

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
        public async Task<IActionResult> Update(string currency, string low, string middle, string high, string action)
        {
            if (action == "remove")
            {
                // Remove Special Pricing logic
                var latestPricing = await _context.Pricing
                    .Where(p => p.IsActive)
                    .OrderByDescending(p => p.CreatedAt)
                    .FirstOrDefaultAsync();

                if (latestPricing != null)
                {
                    latestPricing.IsActive = false;
                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] = "Special pricing removed successfully.";
                }
                else
                {
                    TempData["ErrorMessage"] = "No active special pricing to remove.";
                }

                return RedirectToAction(nameof(Index));
            }

            // Update logic:

            // Step 1: Set all current active rows to inactive
            var activePricings = _context.Pricing.Where(p => p.IsActive);
            foreach (var p in activePricings)
            {
                p.IsActive = false;
            }
            await _context.SaveChangesAsync();

            // Step 2: Insert new active pricing
            decimal lowValue = decimal.Parse(low, System.Globalization.CultureInfo.InvariantCulture);
            decimal middleValue = decimal.Parse(middle, System.Globalization.CultureInfo.InvariantCulture);
            decimal highValue = decimal.Parse(high, System.Globalization.CultureInfo.InvariantCulture);

            var pricing = new Pricing
            {
                Currency = currency,
                Low = lowValue,
                Middle = middleValue,
                High = highValue,
                CreatedAt =  TimeHelper.NowInTurkey(),
                IsActive = true
            };

            _context.Pricing.Add(pricing);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Pricing updated successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}
