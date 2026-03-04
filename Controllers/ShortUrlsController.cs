
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using GittBilSmsCore.Controllers;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using GittBilSmsCore.Services;
using GittBilSmsCore.ViewModels;
using Microsoft.Extensions.Options;

namespace GittiBillSmsCore.Controllers
{
    [Microsoft.AspNetCore.Mvc.Route("[controller]")]
    public class ShortUrlsController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly IStringLocalizer _sharedLocalizer;
        private readonly IShortUrlService _shortUrlService;
        private readonly string _shortUrlBase;

        public ShortUrlsController(
            GittBilSmsDbContext context,
            IStringLocalizerFactory factory,
            IShortUrlService shortUrlService, IOptions<ShortUrlSettings> shortUrlSettings) : base(context)
        {
            _context = context;
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
            _shortUrlService = shortUrlService;
            _shortUrlBase = shortUrlSettings.Value.BaseUrl.TrimEnd('/');
        }

        #region Helper Methods
        private string BuildShortUrl(string shortCode) => $"{_shortUrlBase}/{shortCode}";
        private async Task<bool> CanAccessCompany(int companyId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return false;

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return false;

            // Admins can access all companies
            if (user.UserType == "Admin" || user.UserType == "SuperAdmin")
                return true;

            // Company users can only access their own company
            if (user.UserType == "CompanyUser" || user.UserType == "PanelUser" || user.UserType == "SubUser")
                return user.CompanyId == companyId;

            return false;
        }

        private async Task<int?> GetCurrentUserCompanyId()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return null;

            var user = await _context.Users.FindAsync(userId);
            return user?.CompanyId;
        }

        private async Task<bool> IsAdmin()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return false;

            var user = await _context.Users.FindAsync(userId);
            return user?.UserType == "Admin" || user?.UserType == "SuperAdmin";
        }
        #endregion

        // GET: /ShortUrls
        [HttpGet("")]
        public async Task<IActionResult> Index(int page = 1, string search = "")
        {
            if (!HasAccessRoles("ShortUrls", "Read"))
            {
                return Forbid();
            }

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound();

            // ✅ Admins can see all short URLs, Company users see only their own
            IQueryable<ShortUrl> query = _context.ShortUrls
                .Include(s => s.Company)
                .Include(s => s.CreatedByUser);

            if (user.UserType == "CompanyUser" || user.UserType == "PanelUser" || user.UserType == "SubUser")
            {
                if (user.CompanyId == null)
                    return Unauthorized();

                query = query.Where(s => s.CompanyId == user.CompanyId);
            }
            // Search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim().ToLower();
                query = query.Where(s =>
                    s.ShortCode.ToLower().Contains(search) ||
                    s.OriginalUrl.ToLower().Contains(search) ||
                    (s.Title != null && s.Title.ToLower().Contains(search)) ||
                    s.Company.CompanyName.ToLower().Contains(search) ||
                    s.CreatedByUser.FullName.ToLower().Contains(search)
                );
            }

            var entities = await query
                .OrderByDescending(s => s.CreatedDate)
                .Skip((page - 1) * 20)
                .Take(20)
                .Include(s => s.Company)            
                .Include(s => s.CreatedByUser)     
                .ToListAsync();
            if (!entities.Any() && page > 1)
            {
                return RedirectToAction("Index", new { page = 1,search });
            }
            var shortUrls = entities
                .Select(s => new ShortUrlViewModel   
                {
                    Id = s.Id,
                    ShortCode = s.ShortCode,
                    ShortUrl = BuildShortUrl(s.ShortCode),
                    OriginalUrl = s.OriginalUrl,
                    Title = s.Title,
                    TotalClicks = s.TotalClicks,
                    MaxClicks = s.MaxClicks,
                    IsActive = s.IsActive,
                    CreatedDate = s.CreatedDate,
                    ExpiryDate = s.ExpiryDate,
                    CompanyName = s.Company.CompanyName,       
                    CreatedByName = s.CreatedByUser.FullName  
                })
                .ToList();

            ViewBag.CurrentPage = page;
            ViewBag.TotalCount = await query.CountAsync();
            ViewBag.PageSize = 20;
            ViewBag.HasCreatePermission = HasAccessRoles("ShortUrls", "Create");
            ViewBag.HasEditPermission = HasAccessRoles("ShortUrls", "Edit");
            ViewBag.HasDeletePermission = HasAccessRoles("ShortUrls", "Delete");
            ViewBag.Search = search;
            return View(shortUrls);
        }

        // GET: /ShortUrls/Create
        [HttpGet("Create")]
        public async Task<IActionResult> Create()
        {
            if (!HasAccessRoles("ShortUrls", "Create"))
            {
                return Forbid();
            }

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return Unauthorized();

            var isAdmin = user.UserType == "Admin" || user.UserType == "SuperAdmin";
            ViewBag.IsAdmin = isAdmin;

            if (isAdmin)
            {
                ViewBag.Companies = await _context.Companies
                    .OrderBy(c => c.CompanyName)
                    .Select(c => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                    {
                        Value = c.CompanyId.ToString(),
                        Text = c.CompanyName
                    })
                    .ToListAsync();
            }
            else
            {
                var company = await _context.Companies.FindAsync(user.CompanyId);
                ViewBag.CompanyName = company?.CompanyName ?? "N/A";
                ViewBag.CompanyId = user.CompanyId;
            }

            return View(new CreateShortUrlViewModel());
        }

        private async Task ReloadCreateViewBag(User user, bool isAdmin)
        {
            ViewBag.IsAdmin = isAdmin;
            if (isAdmin)
            {
                ViewBag.Companies = await _context.Companies
                    .OrderBy(c => c.CompanyName)
                    .Select(c => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                    {
                        Value = c.CompanyId.ToString(),
                        Text = c.CompanyName
                    })
                    .ToListAsync();
            }
            else
            {
                var company = await _context.Companies.FindAsync(user.CompanyId);
                ViewBag.CompanyName = company?.CompanyName ?? "N/A";
                ViewBag.CompanyId = user.CompanyId;
            }
        }
        // POST: /ShortUrls/Create
        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateShortUrlViewModel model)
        {
            if (!HasAccessRoles("ShortUrls", "Create"))
            {
                return Forbid();
            }

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return Unauthorized();

            var isAdmin = user.UserType == "Admin" || user.UserType == "SuperAdmin";

            if (!ModelState.IsValid)
            {
                await ReloadCreateViewBag(user, isAdmin);
                return View(model);
            }

            try
            {
                int companyId;
                if (isAdmin)
                {
                    if (model.CompanyId == null || model.CompanyId <= 0)
                    {
                        ModelState.AddModelError("CompanyId", "Please select a company.");
                        await ReloadCreateViewBag(user, isAdmin);
                        return View(model);
                    }
                    companyId = model.CompanyId.Value;
                }
                else
                {
                    if (user.CompanyId == null)
                    {
                        ModelState.AddModelError("", "You are not associated with any company.");
                        await ReloadCreateViewBag(user, isAdmin);
                        return View(model);
                    }
                    companyId = user.CompanyId.Value;
                }

                var result = await _shortUrlService.CreateShortUrlAsync(model, companyId, userId.Value);

                TempData["SuccessMessage"] = _sharedLocalizer["shorturlcreatedsuccess"].Value;
                return RedirectToAction("Edit", new { id = result.Id });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("CustomBackHalf", ex.Message);
                await ReloadCreateViewBag(user, isAdmin);
                return View(model);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", _sharedLocalizer["errorcreatingshortu"].Value + ": " + ex.Message);
                await ReloadCreateViewBag(user, isAdmin);
                return View(model);
            }
        }

        // GET: /ShortUrls/Edit/123
        [HttpGet("Edit/{id}")]
        public async Task<IActionResult> Edit(long id)
        {
            if (!HasAccessRoles("ShortUrls", "Read"))
            {
                return Forbid();
            }

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound();

            var shortUrlEntity = await _context.ShortUrls
                .Include(s => s.Company)
                .Include(s => s.CreatedByUser)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (shortUrlEntity == null)
            {
                TempData["ErrorMessage"] = "Short URL not found";
                return RedirectToAction("Index");
            }

            // Check access
            if (user.UserType != "Admin" && user.UserType != "SuperAdmin")
            {
                if (shortUrlEntity.CompanyId != user.CompanyId)
                {
                    return Forbid();
                }
            }

            var shortUrl = new ShortUrlViewModel
            {
                Id = shortUrlEntity.Id,
                ShortCode = shortUrlEntity.ShortCode,
                ShortUrl = BuildShortUrl(shortUrlEntity.ShortCode),
                OriginalUrl = shortUrlEntity.OriginalUrl,
                Title = shortUrlEntity.Title,
                CreatedDate = shortUrlEntity.CreatedDate,
                ExpiryDate = shortUrlEntity.ExpiryDate,
                TotalClicks = shortUrlEntity.TotalClicks,
                MaxClicks = shortUrlEntity.MaxClicks,
                IsActive = shortUrlEntity.IsActive,
                CompanyName = shortUrlEntity.Company?.CompanyName,
                CreatedByName = shortUrlEntity.CreatedByUser?.FullName
            };

            // ✅ Use EditShortUrlViewModel instead of CreateShortUrlViewModel
            var model = new EditShortUrlViewModel
            {
                DestinationUrl = shortUrl.OriginalUrl,
                Title = shortUrl.Title,
                ExpiryDate = shortUrl.ExpiryDate,
                MaxClicks = shortUrl.MaxClicks
            };

            ViewBag.ShortUrl = shortUrl;
            return View(model);
        }

        // POST: /ShortUrls/Edit/123
        [HttpPost("Edit/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(long id, EditShortUrlViewModel model) // ✅ Changed to EditShortUrlViewModel
        {
            if (!HasAccessRoles("ShortUrls", "Edit"))
            {
                return Forbid();
            }

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound();

            if (!ModelState.IsValid)
            {
                var shortUrlEntity = await _context.ShortUrls
                    .Include(s => s.Company)
                    .Include(s => s.CreatedByUser)
                    .FirstOrDefaultAsync(s => s.Id == id);

                if (shortUrlEntity != null)
                {
                    ViewBag.ShortUrl = new ShortUrlViewModel
                    {
                        Id = shortUrlEntity.Id,
                        ShortCode = shortUrlEntity.ShortCode,
                        ShortUrl = BuildShortUrl(shortUrlEntity.ShortCode),
                        OriginalUrl = shortUrlEntity.OriginalUrl,
                        Title = shortUrlEntity.Title,
                        CreatedDate = shortUrlEntity.CreatedDate,
                        ExpiryDate = shortUrlEntity.ExpiryDate,
                        TotalClicks = shortUrlEntity.TotalClicks,
                        MaxClicks = shortUrlEntity.MaxClicks,
                        IsActive = shortUrlEntity.IsActive
                    };
                }
                return View(model);
            }

            try
            {
                var shortUrlEntity = await _context.ShortUrls.FirstOrDefaultAsync(s => s.Id == id);
                if (shortUrlEntity == null)
                {
                    TempData["ErrorMessage"] = "Short URL not found";
                    return RedirectToAction("Index");
                }

                // Check access
                if (user.UserType != "Admin" && user.UserType != "SuperAdmin")
                {
                    if (shortUrlEntity.CompanyId != user.CompanyId)
                    {
                        return Forbid();
                    }
                }

                // Update fields
                shortUrlEntity.Title = model.Title;
                shortUrlEntity.ExpiryDate = model.ExpiryDate;
                shortUrlEntity.MaxClicks = model.MaxClicks;

                if (shortUrlEntity.OriginalUrl != model.DestinationUrl)
                {
                    string newUrl = model.DestinationUrl;
                    if (!newUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !newUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        newUrl = "https://" + newUrl;
                    }
                    shortUrlEntity.OriginalUrl = newUrl;
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Short URL updated successfully!";
                return RedirectToAction("Edit", new { id });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error updating short URL: " + ex.Message);

                var shortUrlEntity = await _context.ShortUrls
                    .Include(s => s.Company)
                    .Include(s => s.CreatedByUser)
                    .FirstOrDefaultAsync(s => s.Id == id);

                if (shortUrlEntity != null)
                {
                    ViewBag.ShortUrl = new ShortUrlViewModel
                    {
                        Id = shortUrlEntity.Id,
                        ShortCode = shortUrlEntity.ShortCode,
                        ShortUrl = BuildShortUrl(shortUrlEntity.ShortCode),
                        OriginalUrl = shortUrlEntity.OriginalUrl,
                        Title = shortUrlEntity.Title,
                        CreatedDate = shortUrlEntity.CreatedDate,
                        ExpiryDate = shortUrlEntity.ExpiryDate,
                        TotalClicks = shortUrlEntity.TotalClicks,
                        MaxClicks = shortUrlEntity.MaxClicks,
                        IsActive = shortUrlEntity.IsActive
                    };
                }

                return View(model);
            }
        }

        [HttpPost("Delete/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(long id)
        {
            if (!HasAccessRoles("ShortUrls", "Delete"))
            {
                return Forbid();
            }

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound();

            var shortUrl = await _context.ShortUrls.FirstOrDefaultAsync(s => s.Id == id);
            if (shortUrl == null)
            {
                TempData["ErrorMessage"] = _sharedLocalizer["shorturlnotfound"].Value;
                return RedirectToAction("Index");
            }

            // ✅ Check access
            if (user.UserType != "Admin" && user.UserType != "SuperAdmin")
            {
                if (shortUrl.CompanyId != user.CompanyId)
                {
                    return Forbid();
                }
            }

            shortUrl.IsActive = false;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = _sharedLocalizer["shorturldeletedsuccess"].Value;
            return RedirectToAction("Index");
        }

        // GET: /ShortUrls/GetAll - For DataTables/Ajax
        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAll()
        {
            if (!HasAccessRoles("ShortUrls", "Read"))
            {
                return Forbid();
            }

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound();

            // ✅ Admins see all, Company users see only their own
            IQueryable<ShortUrl> query = _context.ShortUrls;

            if (user.UserType == "CompanyUser" || user.UserType == "PanelUser" || user.UserType == "SubUser")
            {
                if (user.CompanyId == null)
                    return Unauthorized();

                query = query.Where(s => s.CompanyId == user.CompanyId);
            }

            var shortUrls = await query
                .OrderByDescending(s => s.CreatedDate)
                .Select(s => new
                {
                    s.Id,
                    s.ShortCode,
                    ShortUrl = BuildShortUrl(s.ShortCode),
                    s.OriginalUrl,
                    s.Title,
                    s.TotalClicks,
                    s.MaxClicks,
                    s.IsActive,
                    s.CreatedDate,
                    s.ExpiryDate,
                    CreatedBy = s.CreatedByUser.FullName
                })
                .ToListAsync();

            return Json(shortUrls);
        }

        // POST: /ShortUrls/ToggleActive/{id}
        [HttpPost("ToggleActive/{id}")]
        public async Task<IActionResult> ToggleActive(long id)
        {
            if (!HasAccessRoles("ShortUrls", "Edit"))
            {
                return Forbid();
            }

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound();

            var shortUrl = await _context.ShortUrls.FirstOrDefaultAsync(s => s.Id == id);
            if (shortUrl == null)
                return NotFound();

            // ✅ Check access
            if (user.UserType != "Admin" && user.UserType != "SuperAdmin")
            {
                if (shortUrl.CompanyId != user.CompanyId)
                {
                    return Forbid();
                }
            }

            shortUrl.IsActive = !shortUrl.IsActive;
            await _context.SaveChangesAsync();

            return Ok(new { isActive = shortUrl.IsActive });
        }
    }
}