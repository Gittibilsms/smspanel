using Microsoft.AspNetCore.Mvc;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GittBilSmsCore.Helpers;

namespace GittBilSmsCore.Controllers
{
    [Route("Admin")]
    public class AdminController : BaseController
    {
        private readonly GittBilSmsDbContext _context;

        public AdminController(GittBilSmsDbContext context) : base(context)
        {
            _context = context;
        }


 
        // BannedNumbers
        [HttpGet("Banned")]
        public async Task<IActionResult> Banned()
        {
            var list = await _context.BannedNumbers.OrderByDescending(x => x.CreatedAt).ToListAsync();
            return View(list);
        }

        [HttpPost("Banned/Add")]
        public async Task<IActionResult> AddBanned(string number, string reason)
        {
            if (!string.IsNullOrWhiteSpace(number))
            {
                var entry = new BannedNumber
                {
                    Number = number.Trim(),
                    Reason = reason?.Trim()
                };

                _context.BannedNumbers.Add(entry);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Banned");
        }

        [HttpPost("Banned/Delete")]
        public async Task<IActionResult> DeleteBanned(int id)
        {
            var entry = await _context.BannedNumbers.FindAsync(id);
            if (entry != null)
            {
                _context.BannedNumbers.Remove(entry);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Banned");
        }
    }
}