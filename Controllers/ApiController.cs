using Microsoft.AspNetCore.Mvc;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using GittBilSmsCore.Helpers;
using Microsoft.Extensions.Localization;
namespace GittBilSmsCore.Controllers
{
    public class ApiController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly IStringLocalizer _sharedLocalizer;
        public ApiController(GittBilSmsDbContext context, IStringLocalizerFactory factory) : base(context)
        {
            _context = context;
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
        }
        public IActionResult Index()
        {
            var roleId = HttpContext.Session.GetInt32("RoleId");

            if (roleId != 1) // 1 = Admin
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var apis = _context.Apis.Where(a => a.IsActive && !a.IsClientApi).ToList();
            return View(apis); 
        }
        [HttpPost]
        public async Task<IActionResult> Create([FromForm] Api model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.ServiceName) ||
                    string.IsNullOrWhiteSpace(model.Username) ||
                    string.IsNullOrWhiteSpace(model.Password) ||
                    string.IsNullOrWhiteSpace(model.Originator) ||
                    string.IsNullOrWhiteSpace(model.ApiUrl))
                {
                    return Json(new { success = false, message = _sharedLocalizer["allfieldsrequired"] });
                }

                model.CreatedAt = TimeHelper.NowInTurkey();
                model.IsActive = true;
                model.ContentType ??= "application/json";
                model.ServiceType = model.ServiceType ?? "SMS";

                // Get user ID if available from session/claims
                model.CreatedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0;

                _context.Apis.Add(model);
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Log exception here if needed
                return Json(new { success = false, message = ex.Message });
            }
        }
        [HttpPost]
        public async Task<IActionResult> Update([FromForm] Api updated)
        {
            var api = await _context.Apis.FindAsync(updated.ApiId);
            if (api == null) return Json(new { success = false, message = _sharedLocalizer["apinotfound"] });

            api.ServiceName = updated.ServiceName;
            api.ServiceType = updated.ServiceType;
            api.Username = updated.Username;
            api.Password = updated.Password;
            api.Originator = updated.Originator;
            api.UpdatedAt = TimeHelper.NowInTurkey();

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var api = await _context.Apis.FindAsync(id);
            if (api == null) return Json(new { success = false, message = _sharedLocalizer["apinotfound"] });

            _context.Apis.Remove(api);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> MakeDefault(int id)
        {
            var selectedApi = await _context.Apis.FindAsync(id);
            if (selectedApi == null)
                return Json(new { success = false, message = _sharedLocalizer["apinotfound"] });

            // Unset all current defaults
            var allApis = _context.Apis.Where(a => a.IsDefault);
            foreach (var api in allApis)
            {
                api.IsDefault = false;
            }

            // Set the selected one
            selectedApi.IsDefault = true;
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        public IActionResult List()
        {
            var apis = _context.Apis.Where(a => a.IsActive && !a.IsClientApi).ToList();
            return PartialView("_ApiCards", apis);
        }

    }
}
