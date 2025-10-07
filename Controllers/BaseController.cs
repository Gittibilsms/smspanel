using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace GittBilSmsCore.Controllers
{
    public class BaseController : Controller
    {
        private readonly GittBilSmsDbContext _context;
        public BaseController(GittBilSmsDbContext context)
        {
            _context = context;
        }


        public override async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {

            var controller = context.RouteData.Values["controller"]?.ToString()?.ToLower();
            var action = context.RouteData.Values["action"]?.ToString()?.ToLower();

            // Skip checks for login/logout/etc.
            if (controller == "account" &&
               (action == "login" || action == "logout" ||
                action == "verify2fa" || action == "verifyemail2fa"))
            {
                await next();
                return;
            }

            // Session‐expired?
            if (!HttpContext.Session.Keys.Contains("UserId"))
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            // Populate user info into ViewBag
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId.HasValue)
            {
                var user = await _context.Users.FindAsync(userId.Value);
                if (user != null)
                {
                    ViewBag.ProfilePhotoUrl = string.IsNullOrEmpty(user.ProfilePhotoUrl)
                        ? "/assets/images/avatars/01.png"
                        : user.ProfilePhotoUrl;
                    ViewBag.FullName = user.FullName ?? "User";
                }
            }

            // **HERE**: compute your live “new support” badge
            // (this is async so we can await EF Core)
            var roleId = HttpContext.Session.GetInt32("RoleId") ?? 0;
            var userType = HttpContext.Session.GetString("UserType") ?? "";
            var isMainUser = HttpContext.Session.GetInt32("IsMainUser") == 1;
            var companyId = HttpContext.Session.GetInt32("CompanyId");
            var thisUserId = userId.Value;

            int badgeCount;

            if (roleId == 1)
            {
                // Admin: count *all* open tickets
                badgeCount = await _context.Tickets
                    .CountAsync(t => t.Status == TicketStatus.Open);
            }
            else if (string.Equals(userType, "CompanyUser", StringComparison.OrdinalIgnoreCase))
            {
                if (isMainUser && companyId.HasValue)
                {
                    // Main company user: count open tickets *for this company*
                    badgeCount = await _context.Tickets
                        .Where(t => t.Status == TicketStatus.Open
                                 && t.CreatedByUser.CompanyId == companyId.Value)
                        .CountAsync();
                }
                else
                {
                    // Sub‑user: count only the ones *he* raised
                    badgeCount = await _context.Tickets
                        .Where(t => t.Status == TicketStatus.Open
                                 && t.CreatedByUserId == thisUserId)
                        .CountAsync();
                }
            }
            else
            {
                // all other roles: no badge (or adjust for “support staff” if needed)
                badgeCount = 0;
            }

            ViewBag.SupportBadgeCount = badgeCount;

            // finally call the action
            await next();
        }
        protected bool HasAccessRoles(string module, string action)
        {
            var permissionsJson = HttpContext.Session.GetString("UserPermissions") ?? "[]";
            var rolePermissions = JsonConvert.DeserializeObject<List<RolePermission>>(permissionsJson) ?? new List<RolePermission>();

            return rolePermissions.Any(p =>
                string.Equals(p.Module, module, StringComparison.OrdinalIgnoreCase) &&
                (action == "Read" && p.CanRead ||
                 action == "Create" && p.CanCreate ||
                 action == "Edit" && p.CanEdit ||
                 action == "Delete" && p.CanDelete));
        }
        public bool HasPermission(string permissionKey)
        {
            var permissionsString = HttpContext.Session.GetString("Permissions") ?? "";
            var permissions = permissionsString.Split(",", StringSplitOptions.RemoveEmptyEntries).ToList();
            return permissions.Contains(permissionKey);
        }
    }
}