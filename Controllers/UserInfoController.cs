using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using Microsoft.AspNetCore.Identity;

namespace GittBilSmsCore.Controllers
{
    public class UserInfoController : BaseController
    {
        private readonly GittBilSmsDbContext _context;

        public UserInfoController(GittBilSmsDbContext context, UserManager<User> userManager) : base(context)
        {
            _context = context;
        }
        public async Task<IActionResult> Index()
        {
            var roleId = HttpContext.Session.GetInt32("RoleId");

            if (roleId != 1) // 1 = Admin
            {
                return RedirectToAction("AccessDenied", "Account");
            }
            var adminUsernames = await _context.Users
                 .Where(u => u.UserType == "Admin")
                 .Select(u => u.UserName)
                 .ToListAsync();

            var logins = await _context.LoginHistories
                .Where(l => !adminUsernames.Contains(l.Username))
                .OrderByDescending(l => l.LoggedInAt)
                .Take(200)
                .ToListAsync();

            return View(logins);
        }
    }
}
