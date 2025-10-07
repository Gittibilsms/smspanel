using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Spreadsheet;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using GittBilSmsCore.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GittBilSmsCore.Controllers
{
    public class BlacklistController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly IStringLocalizer _sharedLocalizer;
        private readonly IWebHostEnvironment _env;

        public BlacklistController(GittBilSmsDbContext context, IStringLocalizerFactory factory, IWebHostEnvironment env) : base(context)
        {
            _context = context;
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
            _env = env;
        }

        public IActionResult Index()
        {
            if (!HasAccessRoles("Blacklist", "Read"))
            {
                return Forbid(); 
            }
            return View(new BlacklistViewModel());
        }
        [HttpPost]
        public async Task<IActionResult> Add(BlacklistViewModel model)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var companyId = HttpContext.Session.GetInt32("CompanyId");

            if (userId == null || companyId == null)
                return Json(new { success = false, message = "Unauthorized" });

            var numbers = ParseNumbers(model.PhoneNumbersInput);

            foreach (var number in numbers)
            {
                if (!_context.BlacklistNumbers.Any(b => b.Number == number && b.CompanyId == companyId))
                {
                    _context.BlacklistNumbers.Add(new BlacklistNumber
                    {
                        Number = number,
                        CompanyId = companyId.Value,
                        CreatedByUserId = userId.Value,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> Remove(BlacklistViewModel model)
        {
            var numbers = ParseNumbers(model.PhoneNumbersInput);
            var items = _context.BlacklistNumbers.Where(b => numbers.Contains(b.Number));

            _context.BlacklistNumbers.RemoveRange(items);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }
       private (int userId, int companyId)? GetUserContext()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var companyId = HttpContext.Session.GetInt32("CompanyId");

            if (userId == null || companyId == null) return null;

            return (userId.Value, companyId.Value);
        }

        [HttpPost]
        public async Task<IActionResult> UploadFromFile(IFormFile file)
        {
            var context = GetUserContext(); // your existing helper
            if (context == null)
                return Json(new { success = false, message = "Unauthorized." });

            var (userId, companyId) = context.Value;

            if (file == null || file.Length == 0)
                return Json(new { success = false, message = "Invalid file." });

            using var reader = new StreamReader(file.OpenReadStream());
            var content = await reader.ReadToEndAsync();

            var numbers = ParseNumbers(content);

            int added = 0;
            foreach (var number in numbers)
            {
                bool exists = await _context.BlacklistNumbers
                    .AnyAsync(b => b.Number == number && b.CompanyId == companyId);

                if (!exists)
                {
                    _context.BlacklistNumbers.Add(new BlacklistNumber
                    {
                        Number = number,
                        CompanyId = companyId,
                        CreatedByUserId = userId
                    });
                    added++;
                }
            }

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = _sharedLocalizer["blacklistaddedsuccess", added]
            });
        }
        [HttpPost]
        public IActionResult Search(BlacklistViewModel model)
        {
            model.SearchPerformed = true;
            model.SearchResultFound = _context.BlacklistNumbers.Any(b => b.Number == model.SearchPhoneNumber);
            return View("Index", model);
        }

        private List<string> ParseNumbers(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<string>();

            return input
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p =>
                    !string.IsNullOrWhiteSpace(p) &&
                    !Regex.IsMatch(p, @"[a-zA-Z]") &&  
                    p.Length <= 50               
                )
                .Distinct()
                .ToList();
        }

    }
}
