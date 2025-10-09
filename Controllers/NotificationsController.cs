using GittBilSmsCore.Models;
using GittBilSmsCore.Services;
using Microsoft.AspNetCore.Mvc;
using GittBilSmsCore.Helpers;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using GittBilSmsCore.Enums;
using System.Security.Claims;

namespace GittBilSmsCore.Controllers
{
    public class NotificationsController : Controller
    {
        private readonly INotificationService _notificationService;

        public NotificationsController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public async Task<IActionResult> Index()
        {
            // Example: Get the CompanyId from session (adjust as per your auth logic)
            int? companyId = HttpContext.Session.GetInt32("CompanyId");

            var notifications = await _notificationService.GetUnreadNotificationsAsync(companyId, User);
            return View(notifications); // Pass to View
        }
        public async Task<string> RenderPartialViewToString(Controller controller, string viewName, object model)
        {
            controller.ViewData.Model = model;

            using (var writer = new StringWriter())
            {
                var viewEngine = controller.HttpContext.RequestServices.GetService(typeof(ICompositeViewEngine)) as ICompositeViewEngine;

                var viewResult = viewEngine.FindView(controller.ControllerContext, viewName, false);

                if (viewResult.View == null)
                {
                    throw new ArgumentNullException($"View '{viewName}' not found.");
                }

                var viewContext = new ViewContext(
                    controller.ControllerContext,
                    viewResult.View,
                    controller.ViewData,
                    controller.TempData,
                    writer,
                    new HtmlHelperOptions()
                );

                await viewResult.View.RenderAsync(viewContext);
                return writer.GetStringBuilder().ToString();
            }
        }
        public async Task<IActionResult> Load()
        {
            int? companyId = RoleHelper.HasGlobalAccess(User) ? null : HttpContext.Session.GetInt32("CompanyId");
            var notifications = await _notificationService.GetUnreadNotificationsAsync(companyId, User);

            return Json(new
            {
                html = await RenderPartialViewToString(this, "_NotificationList", notifications),
                count = notifications.Count()
            });
        }
        [HttpPost]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            await _notificationService.MarkAsReadAsync(id);
            return Json(new { success = true });
        }
        [HttpPost]
        public async Task<IActionResult> MarkAllAsRead()
        {
            try
            {
                int? companyId = RoleHelper.HasGlobalAccess(User)
                    ? null
                    : HttpContext.Session.GetInt32("CompanyId");

                await _notificationService.MarkAllAsReadAsync(companyId);

                return Json(new { success = true, message = "All notifications marked as read." });
            }
            catch (Exception ex)
            {
                // Log the error here if needed
                return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
            }
        }
        [HttpGet]
        public async Task<IActionResult> GetUnread()
        {
            bool isAdmin = RoleHelper.HasGlobalAccess(User);
            int? companyId = isAdmin
                ? null
                : HttpContext.Session.GetInt32("CompanyId");

            // 1) Base fetch
            var notifications = await _notificationService
                .GetUnreadNotificationsAsync(companyId, User);

            // 2) Admins see only awaiting approval
            if (isAdmin)
            {
                notifications = notifications
                    .Where(n => n.Type == NotificationType.SmsAwaitingApproval)
                    .ToList();
                return Json(notifications);
            }

            // 3) Not an admin: figure out main vs sub
            // Make sure your session actually sets "IsMainUser" to 1 / 0
            bool isMainUser = HttpContext.Session.GetInt32("IsMainUser") == 1;

            if (!isMainUser)
            {
                // 4) Safely parse the current user’s ID
                var idClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (idClaim == null || !int.TryParse(idClaim.Value, out var currentUserId))
                {
                    // not logged in or malformed claim
                    return Unauthorized();
                }

                // 5) Filter out any notifications not explicitly addressed to this sub-user
                notifications = notifications
                    .Where(n => n.UserId.HasValue && n.UserId.Value == currentUserId)
                    .ToList();
            }

            return Json(notifications);
        }
    }
}
