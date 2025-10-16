using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using GittBilSmsCore.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GittBilSmsCore.Helpers;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Identity;
using DocumentFormat.OpenXml.ExtendedProperties;
namespace GittBilSmsCore.Controllers
{

    [Microsoft.AspNetCore.Mvc.Route("[controller]")]
    public class CompaniesController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly IStringLocalizer _sharedLocalizer;
        private readonly UserManager<User> _userManager;
        private readonly TelegramMessageService _svc;
        public CompaniesController(GittBilSmsDbContext context, IStringLocalizerFactory factory, UserManager<User> userManager, TelegramMessageService svc) : base(context)
        {
            _context = context;
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
            _userManager = userManager;
            _svc = svc;
        }
        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            if (!HasAccessRoles("Firm", "Read"))
            {
                return Forbid(); // or RedirectToAction("AccessDenied", "Account")
            }
            var latestPricing = await _context.Pricing
                 .Where(p => p.IsActive == true)
                 .OrderByDescending(p => p.CreatedAt)
                 .FirstOrDefaultAsync();

            var viewModel = new AddCompanyViewModel
            {
                LowPrice = latestPricing?.Low ?? 0.23m,
                MediumPrice = latestPricing?.Middle ?? 0.23m,
                HighPrice = latestPricing?.High ?? 0.23m
            };

            return View(viewModel);
        }
        [HttpGet("AddCompanyModal")]
        public async Task<IActionResult> AddCompanyModal()
        {
            var latestPricing = await _context.Pricing
                .Where(p => p.IsActive)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            var apiList = await _context.Apis
                .Where(a => !a.IsClientApi)
                .OrderBy(a => a.ServiceName)
                .ToListAsync();

            var defaultApi = apiList.FirstOrDefault(a => a.IsDefault);

            var apiSelectList = apiList.Select(api => new SelectListItem
            {
                Value = api.ApiId.ToString(),
                Text = api.ApiId == defaultApi?.ApiId ? "Default api" : api.ServiceName,
                Selected = api.ApiId == defaultApi?.ApiId
            }).ToList();

            var viewModel = new AddCompanyViewModel
            {
                LowPrice = latestPricing?.Low ?? 0.23m,
                MediumPrice = latestPricing?.Middle ?? 0.23m,
                HighPrice = latestPricing?.High ?? 0.23m,
                ApiSelectList = apiSelectList
            };

            return PartialView("_AddCompanyModal", viewModel);
        }

        [HttpPost("Add")]
        public async Task<IActionResult> AddCompany([FromBody] AddCompanyInputModel model)
        {
            // 🔍 Check for duplicate company name
            if (await _context.Companies.AnyAsync(c => c.CompanyName == model.CompanyName))
            {
                var localizedError = _sharedLocalizer["companynameexists", model.CompanyName].Value;

                return BadRequest(new
                {
                    success = false,
                    errors = new[] { localizedError }
                });
            }

            // 🔁 Wrap in transaction
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                int? selectedApid = model.Apid;
                if (selectedApid == null)
                {
                    var defaultApi = await _context.Apis.FirstOrDefaultAsync(a => a.IsDefault);
                    if (defaultApi != null)
                    {
                        selectedApid = defaultApi.ApiId;
                    }
                }

                var company = new GittBilSmsCore.Models.Company
                {
                    CompanyName = model.CompanyName,
                    IsTrustedSender = model.IsTrustedSender,
                    IsRefundable = model.IsRefundable,
                    CanSendSupportRequest = model.CanSendSupportRequest ?? true,
                    Apid = selectedApid,
                    CurrencyCode = model.CurrencyCode,
                    LowPrice = model.LowPrice,
                    MediumPrice = model.MediumPrice,
                    Pricing = "Standard",
                    HighPrice = model.HighPrice,
                    CreditLimit = 0,
                    CurrentBalance = 0,
                    IsActive = true,
                    CreatedAt = TimeHelper.NowInTurkey()
                };

                _context.Companies.Add(company);
                await _context.SaveChangesAsync();

                var user = new User
                {
                    CompanyId = company.CompanyId,
                    FullName = model.FullName,
                    UserName = model.UserName,
                    Email = model.Email,
                    PhoneNumber = model.Phone,
                    IsMainUser = true,
                    UserType = "CompanyUser",
                    IsActive = true,
                    CreatedAt = TimeHelper.NowInTurkey()
                };

                var result = await _userManager.CreateAsync(user, model.Password);

                if (!result.Succeeded)
                {
                    // ❌ Rollback and return errors
                    await transaction.RollbackAsync();
                    var localizedErrors = result.Errors.Select(e =>
                          e.Code == "DuplicateUserName"
                              ? _sharedLocalizer["usernametaken", model.UserName].Value
                              : _sharedLocalizer[e.Description].Value
                      );

                    return BadRequest(new
                    {
                        success = false,
                        errors = localizedErrors
                    });
                }

                await _userManager.AddToRoleAsync(user, "CompanyUser");

                _context.UserRoles.Add(new UserRole
                {
                    UserId = user.Id,
                    Name = "Company User",
                    RoleId = 5 // CompanyUser role ID
                });
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new
                {
                    success = false,
                    errors = new[] { "An unexpected error occurred.", ex.Message }
                });
            }
        }

        [HttpGet("GetAll")]
        public async Task<IActionResult> GetAllCompanies()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound();


            IQueryable<GittBilSmsCore.Models.Company> query = _context.Companies.Include(c => c.Api);
            if (user.UserType == "CompanyUser")
            {
                query = query.Where(c => c.CompanyId == user.CompanyId);
            }

            var companies = await query
                .OrderByDescending(c => c.CompanyId)
                .Select(c => new {
                    c.CompanyId,
                    c.CompanyName,
                    c.IsActive,
                    c.CreditLimit,
                    c.CurrencyCode,
                    c.Pricing,
                    c.Apid,
                    ApiName = c.Api.ServiceName,
                    c.IsTrustedSender,
                    c.CanSendSupportRequest,
                    c.IsRefundable,
                    c.CreatedAt,
                    c.UpdatedAt
                })
                .ToListAsync();

            return Json(companies);
        }
        [HttpPost("ToggleActive/{id}")]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var company = await _context.Companies.FindAsync(id);
            if (company == null) return NotFound();

            company.IsActive = !company.IsActive;
            company.UpdatedAt = DateTime.UtcNow.AddHours(3);
            await _context.SaveChangesAsync();

            return Ok(new { isActive = company.IsActive });
        }

        [HttpGet("List")]
        public IActionResult GetCompaniesList()
        {
            var companies = _context.Companies
                .Where(c => c.IsActive)
                .Select(c => new {
                    c.CompanyId,
                    c.CompanyName
                }).ToList();

            return Json(companies);
        }
        [HttpGet("Details/{id}")]
        public async Task<IActionResult> Details(int id)
        {
            var company = await _context.Companies
                .Include(c => c.Api)
                .FirstOrDefaultAsync(c => c.CompanyId == id);

            if (company == null)
                return NotFound();

            var creditTransactions = await _context.CreditTransactions
                .Where(t => t.CompanyId == id)
                .OrderByDescending(t => t.CreditTransactionId)
                .ToListAsync();

            var companyUsers = await _context.Users
                .Where(u => u.CompanyId == id)
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();

            var apiList = await _context.Apis
                .Where(api => !api.IsClientApi)
                .ToListAsync();

            var latestPricing = await _context.Pricing
                .Where(p => p.IsActive)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            var latestUnitPrice = latestPricing != null
                ? latestPricing.Low.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                : "0";

            var distinctPrices = new List<decimal>
            {
                company.LowPrice ?? -1,
                company.MediumPrice ?? -1,
                company.HighPrice ?? -1
            }.Where(p => p > 0).Distinct().OrderBy(p => p).ToList();

            distinctPrices = distinctPrices
                .Distinct()
                .OrderBy(p => p)
                .ToList();
            var defaultApiId = apiList.FirstOrDefault(a => a.IsDefault)?.ApiId;

            var apiSelectList = new List<SelectListItem>();

            var userApiId = company.Apid > 0 ? company.Apid : null;
            var defaultApi = apiList.FirstOrDefault(a => a.IsDefault);

            if (defaultApi != null)
            {
                apiSelectList.Add(new SelectListItem
                {
                    Value = defaultApi.ApiId.ToString(),
                    Text = $"Default API - {defaultApi.ServiceName}",
                    Selected = userApiId == null || userApiId == defaultApi.ApiId
                });
            }

            // Then add all APIs (including default one again)
            apiSelectList.AddRange(apiList
      .Where(api => api.ApiId != defaultApi?.ApiId)
      .Select(api => new SelectListItem
      {
          Value = api.ApiId.ToString(),
          Text = api.ServiceName,
          Selected = userApiId != null && userApiId == api.ApiId
      }));
            var viewModel = new CompanyDetailsViewModel
            {
                Company = company,
                CreditTransactions = creditTransactions,
                CompanyUsers = companyUsers,
                ApiList = apiSelectList,
                LatestUnitPrice = latestUnitPrice,
                DistinctPricingOptions = distinctPrices
            };

            return View(viewModel);
        }
        [HttpGet("GetUsersByCompany")]
        public async Task<IActionResult> GetUsersByCompany([FromQuery] int companyId)
        {
            var users = await _context.Users
                .Include(u => u.Company)
                .Where(u => u.CompanyId == companyId)
                .ToListAsync();

            var result = users
                .OrderByDescending(u => u.IsMainUser)
                .ThenByDescending(u => u.UpdatedAt ?? u.CreatedAt) // ✅ Prefer updated date, fallback to created
                .Select(u => new {
                    u.Id,
                    u.UserName,
                    CompanyName = u.Company?.CompanyName,
                    u.FullName,
                    u.Email,
                    u.PhoneNumber,
                    u.IsActive,
                    u.Quota,
                    u.QuotaType,
                    TwoFA = u.VerificationType,
                    CreatedAt = u.CreatedAt.ToString("yyyy-MM-dd"),
                    UpdatedAt = u.UpdatedAt.HasValue ? u.UpdatedAt.Value.ToString("yyyy-MM-dd") : "",
                    u.IsMainUser
                });

            return Json(result);
        }
        [HttpPost("UpdateDetails")]
        public async Task<IActionResult> UpdateDetails(CompanyDetailsViewModel model)
        {
            try
            {
                var company = await _context.Companies.FindAsync(model.Company.CompanyId);
                if (company == null)
                {
                    TempData["ErrorMessage"] = "Company not found.";
                    return RedirectToAction("Index");
                }

                // Update fields
                company.CompanyName = model.Company.CompanyName;
                company.IsTrustedSender = model.Company.IsTrustedSender;
                company.IsRefundable = model.Company.IsRefundable;
                company.CanSendSupportRequest = model.Company.CanSendSupportRequest;
                company.CurrencyCode = model.Company.CurrencyCode; // ✅ This will now be non-null
                company.LowPrice = model.Company.LowPrice;
                company.MediumPrice = model.Company.MediumPrice;
                company.HighPrice = model.Company.HighPrice;
                company.Apid = model.Company.Apid;
                company.UpdatedAt = TimeHelper.NowInTurkey();

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = _sharedLocalizer["companyupdatedsuccess"].Value;
                return RedirectToAction("Details", new { id = company.CompanyId });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Şirket güncellenirken hata oluştu: {ex.Message}";
                return RedirectToAction("Details", new { id = model.CompanyId });
            }
        }
        [HttpPost("AddCredit")]
        public async Task<IActionResult> AddCredit(int companyId, decimal price, string unitPrice, string currency, string note)
        {
            try
            {
                // ✅ Parse unitPrice using invariant culture
                if (!decimal.TryParse(unitPrice, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal unitPriceValue))
                {
                    return Json(new
                    {
                        success = false,
                        message = _sharedLocalizer["Invalid_Unit_Price"]
                    });
                }

                if (unitPriceValue <= 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = _sharedLocalizer["Unit_Price_Must_Be_Greater_Than_Zero"]
                    });
                }

                // ✅ Calculate credit
                decimal credit = Math.Floor(price / unitPriceValue);

                var transaction = new CreditTransaction
                {
                    CompanyId = companyId,
                    TransactionType = _sharedLocalizer["creditadded"],
                    Credit = credit,
                    TotalPrice = price,
                    UnitPrice = unitPriceValue,
                    Currency = currency,
                    Note = note ?? string.Empty,
                    TransactionDate = TimeHelper.NowInTurkey()
                };

                _context.CreditTransactions.Add(transaction);

                var company = await _context.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId);
                if (company != null)
                {
                    company.CreditLimit += credit;
                }

                await _context.SaveChangesAsync();
                int performedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
                decimal? availableCredit = await (
                        from c in _context.Companies
                        join u in _context.Users on c.CompanyId equals u.CompanyId
                        where u.IsMainUser == true && c.CompanyId == companyId
                        select (decimal?)c.CreditLimit
                    ).FirstOrDefaultAsync();

                var textMsg = string.Format(
                                      _sharedLocalizer["Creditaddedmessage"],
                                      credit,
                                      availableCredit
                                  );
                var companyName = company?.CompanyName ?? "UnknownCompany";
                var textMsgtoAdmin = string.Format(
                                      _sharedLocalizer["CreditaddedmessagetoAdmin"],
                                      companyName,
                                      credit,
                                      availableCredit
                                  );

                var userName = _context.Users.Find(performedByUserId)?.UserName ?? "UnknownUser";

                string dataJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Message = "Credit Added by : " + userName,
                    TelegramMessage = textMsg,
                    Time = TimeHelper.NowInTurkey(),
                    IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = Request.Headers["User-Agent"].ToString()
                });

                await _svc.SendToUsersAsync(companyId, performedByUserId, textMsg, dataJson,textMsgtoAdmin,0);
                return Json(new
                {
                    success = true,
                    message = _sharedLocalizer["creditaddedsuccess"],
                    newCredit = credit,
                    newBalance = company?.CreditLimit
                });
            }
            catch (Exception ex)
            {
                // Optional: log ex
                return Json(new
                {
                    success = false,
                    message = _sharedLocalizer["erroraddingcredit"],
                    error = ex.Message
                });
            }
        }

        [HttpPost("DeleteCredit")]
        public async Task<IActionResult> DeleteCredit(int companyId, decimal credit, string currency, string note)
        {
            var transaction = new CreditTransaction
            {
                CompanyId = companyId,
                TransactionType = "Credit deleted",
                Credit = -credit,
                TotalPrice = 0,
                UnitPrice = 0,
                Currency = currency,
                Note = note ?? string.Empty,
                TransactionDate = DateTime.UtcNow.AddHours(3)
            };

            _context.CreditTransactions.Add(transaction);

            var company = await _context.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId);
            if (company != null)
            {
                // 💡 If credits are stored in CurrentBalance (usually yes):
                company.CurrentBalance -= credit;

                // Optional: Also update CreditLimit if you want to reduce the max cap
                company.CreditLimit -= credit;

                await _context.SaveChangesAsync();
            }
            decimal? availableCredit = await (
                       from c in _context.Companies
                       join u in _context.Users on c.CompanyId equals u.CompanyId
                       where u.IsMainUser == true && c.CompanyId == companyId
                       select (decimal?)c.CreditLimit
                   ).FirstOrDefaultAsync();
            int performedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
            var textMsg = string.Format(
                                       _sharedLocalizer["Creditdeletedmessage"],
                                       credit,
                                       availableCredit
                                   );
            var companyName = company?.CompanyName ?? "UnknownCompany";
            var textMsgtoAdmin = string.Format(
                                       _sharedLocalizer["CreditdeletedmessagetoAdmin"],
                                       credit,
                                       availableCredit
                                   );
            var userName = _context.Users.Find(performedByUserId)?.UserName ?? "UnknownUser";
            string dataJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                Message = "Credit Deleted by : " + userName,
                TelegramMessage = textMsg,
                Time = TimeHelper.NowInTurkey(),
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString()
            });
            await _svc.SendToUsersAsync(companyId, performedByUserId, textMsg, dataJson, textMsgtoAdmin, 0);
            return RedirectToAction("Index", new { companyId });
        }
        [HttpPost("Deactivate/{id}")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var company = await _context.Companies.FindAsync(id);
            if (company == null) return NotFound();

            company.IsActive = false;
            await _context.SaveChangesAsync();

            return Ok();
        }


        [HttpDelete("Delete/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var company = await _context.Companies
        .Include(c => c.Users) // Include related users
        .FirstOrDefaultAsync(c => c.CompanyId == id);

            if (company == null)
                return NotFound();

            // Delete company users first
            if (company.Users != null && company.Users.Any())
            {
                _context.Users.RemoveRange(company.Users);
            }

            _context.Companies.Remove(company);
            await _context.SaveChangesAsync();

            return Ok();
        }

    }
}
