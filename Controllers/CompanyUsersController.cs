using DocumentFormat.OpenXml.Spreadsheet;
using GittBilSmsCore.Data;
using GittBilSmsCore.Helpers;
using GittBilSmsCore.Models;
using GittBilSmsCore.Models;
using GittBilSmsCore.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Telegram.Bot.Types;
using User = GittBilSmsCore.Models.User;
namespace GittBilSmsCore.Controllers
{

    public class CompanyUsersController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly IStringLocalizer _sharedLocalizer;
        private readonly UserManager<User> _userManager;
        private readonly TelegramMessageService _svc;
        public CompanyUsersController(GittBilSmsDbContext context, IStringLocalizerFactory factory, UserManager<User> userManager, TelegramMessageService svc) : base(context)
        {
            _context = context;
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
            _userManager = userManager;
            _svc = svc;
        }

        #region Authorization Helpers

        /// <summary>
        /// Gets the current logged-in user from session
        /// </summary>
        private async Task<User?> GetCurrentUser()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return null;
            return await _context.Users.FindAsync(userId);
        }

        /// <summary>
        /// Checks if the current user is an admin
        /// </summary>
        private async Task<bool> IsAdmin()
        {
            var user = await GetCurrentUser();
            return user?.UserType == "Admin" || user?.UserType == "SuperAdmin";
        }
        private bool IsCompanyBoundUser(User user)
        {
            return user.CompanyId != null && user.CompanyId > 0;
        }
        /// <summary>
        /// Checks if the current user can access the specified company's data
        /// </summary>
        private async Task<bool> CanAccessCompany(int? companyId)
        {
            if (companyId == null || companyId <= 0) return false;

            var currentUser = await GetCurrentUser();
            if (currentUser == null) return false;

            // ✅ User has CompanyId → Can only access their own company
            if (IsCompanyBoundUser(currentUser))
            {
                return currentUser.CompanyId == companyId;
            }

            // ✅ User has NO CompanyId → Admin/PanelUser, check permissions
            return HasAccessRoles("Firm", "Read") || HasAccessRoles("Firm", "Edit");
        }

        /// <summary>
        /// Checks if the current user can access/modify the specified user
        /// </summary>
        private async Task<bool> CanAccessUser(int targetUserId)
        {
            var currentUser = await GetCurrentUser();
            if (currentUser == null) return false;

            // ✅ User has NO CompanyId → Admin/PanelUser
            if (!IsCompanyBoundUser(currentUser))
            {
                // If they have permission, they can access all users
                return HasAccessRoles("Company_User", "Read") || HasAccessRoles("Company_User", "Edit");
            }

            // ✅ User HAS CompanyId → Company-bound user
            var targetUser = await _context.Users.FindAsync(targetUserId);
            if (targetUser == null) return false;

            // Must be in the same company
            if (currentUser.CompanyId != targetUser.CompanyId)
                return false;

            // Main users can access all users in their company
            if (currentUser.IsMainUser == true)
                return true;

            // Users with permission can access all users in their company
            if (HasAccessRoles("Company_User", "Read") || HasAccessRoles("Company_User", "Edit"))
                return true;

            // Fallback: only self + users they created
            return targetUser.CreatedByUserId == currentUser.Id || targetUser.Id == currentUser.Id;
        }


        /// <summary>
        /// Checks if the current user can edit/modify the specified user
        /// </summary>
        private async Task<bool> CanEditUser(int targetUserId)
        {
            var currentUser = await GetCurrentUser();
            if (currentUser == null) return false;

            // Must have edit permission
            if (!HasAccessRoles("Company_User", "Edit"))
                return false;

            // ✅ User has NO CompanyId → Admin/PanelUser with edit permission
            if (!IsCompanyBoundUser(currentUser))
            {
                return true; // Already checked permission above
            }

            // ✅ User HAS CompanyId → Company-bound user
            var targetUser = await _context.Users.FindAsync(targetUserId);
            if (targetUser == null) return false;

            // Must be in the same company
            if (currentUser.CompanyId != targetUser.CompanyId)
                return false;

            // Main users can edit all in their company
            if (currentUser.IsMainUser == true)
                return true;

            // Others can only edit users they created
            return targetUser.CreatedByUserId == currentUser.Id;
        }
        #endregion

        // GET: /CompanyUsers
        public IActionResult Index()
        {
            if (!HasAccessRoles("Company_User", "Read"))
            {
                return Forbid();
            }
            return View(); // no model
        }

        // GET: /CompanyUsers/Create
        public IActionResult Create()
        {
            // Check edit permission
            if (!HasAccessRoles("Company_User", "Create"))
            {
                return Forbid();
            }
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Account");

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login", "Account");

            IQueryable<Company> companyQuery = _context.Companies;

            // Filter if user is CompanyUser
            if (user.UserType == "CompanyUser")
            {
                companyQuery = companyQuery.Where(c => c.CompanyId == user.CompanyId);
            }
            ViewBag.IsEdit = false;
            ViewBag.Companies = companyQuery
                .OrderBy(c => c.CompanyName)
                .Select(c => new SelectListItem
                {
                    Value = c.CompanyId.ToString(),
                    Text = c.CompanyName
                })
                .ToList();

            // ✅ Pass default model
            return View(new User
            {
                CompanyId = user.CompanyId  // Preselect company
            });
        }
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            // Check edit permission
            if (!HasAccessRoles("Company_User", "Edit"))
            {
                return Forbid();
            }

            // Check if user can access this specific user
            if (!await CanAccessUser(id))
            {
                return Forbid();
            }
            var user = await _context.Users.FindAsync(id);

            if (user == null)
            {
                return NotFound(new { success = false, message = "User not found." });
            }

            try
            {
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = _sharedLocalizer["userdeletedsuccess"] });
            }
            catch (DbUpdateException ex)
            {
                // Handle possible foreign key constraint issues
                return StatusCode(500, new { success = false, message = _sharedLocalizer["erroroccureddeletinguser"] });
            }
            catch (Exception)
            {
                return StatusCode(500, new { success = false, message = _sharedLocalizer["erroroccureddeletinguser"] });
            }
        }
        // POST: /CompanyUsers/Create
        [HttpPost]
        public async Task<IActionResult> Create(User model)
        {
            // Check edit permission
            if (!HasAccessRoles("Company_User", "Create"))
            {
                return Forbid();
            }

            // Verify user can create users for this company
            if (!await CanAccessCompany(model.CompanyId))
            {
                return Forbid();
            }

            if (model.CompanyId == null || model.CompanyId == 0)
                return BadRequest(new { success = false, message = "Firma gereklidir." });

            if (string.IsNullOrEmpty(model.UserName) || string.IsNullOrEmpty(model.Password))
                return BadRequest(new { success = false, message = _sharedLocalizer["usernamepasswordrequired"] });

            var existingUser = await _userManager.FindByNameAsync(model.UserName);
            if (existingUser != null)
                return BadRequest(new { success = false, message = _sharedLocalizer["usernameexists"] });
            var userId = HttpContext.Session.GetInt32("UserId");
            var newUser = new User
            {
                CompanyId = model.CompanyId,
                UserName = model.UserName,
                Email = model.Email,
                PhoneNumber = model.PhoneNumber,
                FullName = model.FullName,
                UserType = "CompanyUser",
                CreatedAt = TimeHelper.NowInTurkey(),
                IsActive = true,
                IsMainUser = false,
                CreatedByUserId = userId
            };
            bool isnewuser = false;
            var result = await _userManager.CreateAsync(newUser, model.Password);
            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    success = false,
                    message = string.Join("; ", result.Errors.Select(e => e.Description))
                });
            }

            // ✅ Add to ASP.NET Identity Role (if used)
            await _userManager.AddToRoleAsync(newUser, "CompanyUser");

            // ✅ Manually add to internal RoleId = 5
            var userRole = new UserRole
            {
                UserId = newUser.Id,
                Name = "Company User",
                RoleId = 5
            };
            _context.UserRoles.Add(userRole);
            await _context.SaveChangesAsync();
            isnewuser = true;
            // send alert to admin about new user creation
            if(isnewuser)
            {
                int loggerUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
                var loggedinUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == loggerUserId);
                string loggedInUserName = loggedinUser?.UserName ?? "unknownUser";
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                var userAgent = Request.Headers["User-Agent"].ToString();
                var location = await SessionHelper.GetLocationFromIP(ipAddress);
                var textMsg = string.Format(
                                     _sharedLocalizer["userCreated"], loggedInUserName, model.UserName, ipAddress, location, SessionHelper.ParseDevice(userAgent)
                                 );
                string dataJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Message = "User created into the system : " + model.UserName,
                    TelegramMessage = textMsg,
                    Time = TimeHelper.NowInTurkey(),
                    IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = Request.Headers["User-Agent"].ToString()
                });

                await _svc.UserLogAlertToAdmin(model.CompanyId ?? 0, loggerUserId, textMsg, dataJson);
            }

            return Json(new { success = true });
        }
        [HttpGet]
        public async Task<IActionResult> GetAllCompanyUsers()
        {
            // Check read permission
            if (!HasAccessRoles("Company_User", "Read"))
            {
                return Forbid();
            }
            var companyId = HttpContext.Session.GetInt32("CompanyId");
            var userType = HttpContext.Session.GetString("UserType");
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            var isMainUser = HttpContext.Session.GetInt32("IsMainUser") == 1;

            // Get all users assigned to companies
            var allUsers = await _context.Users
                .Where(u => u.CompanyId != null && u.CompanyId > 0)
                .Include(u => u.Company)
                .ToListAsync();

            // Manual join to get CreatedByUser's name
            var allUserList = (from user in allUsers
                               join creator in _context.Users
                                   on user.CreatedByUserId equals creator.Id into creatorJoin
                               from creator in creatorJoin.DefaultIfEmpty()
                               select new
                               {
                                   user.Id,
                                   user.UserName,
                                   IsActive = user.IsActive ? "✅" : "❌",
                                   user.FullName,
                                   CompanyName = user.Company?.CompanyName,
                                   QuotaType = user.QuotaType ?? "N/A",
                                   Quota = user.Quota ?? 0,
                                   user.Email,
                                   user.PhoneNumber,
                                   telegramUserId = user.TelegramUserId.HasValue ? "✅" : "❌",
                                   TwoFA = (user.IsTwoFactorEnabled == true)
    ? (string.IsNullOrWhiteSpace(user.VerificationType) ? "Enabled" : user.VerificationType)
    : "N/A",
                                   CreatedAt = user.CreatedAt.ToString("dd/MM/yyyy"),
                                   UpdatedAt = user.UpdatedAt.HasValue ? user.UpdatedAt.Value.ToString("dd/MM/yyyy") : "-",
                                   user.IsMainUser,
                                   CreatedBy = creator != null ? creator.FullName : "—",
                                   user.CompanyId,
                                   user.CreatedByUserId
                               }).ToList();

            // Apply permission-based filtering
            if (string.Equals(userType, "CompanyUser", StringComparison.OrdinalIgnoreCase) && companyId.HasValue)
            {
                allUserList = allUserList
                    .Where(u => u.CompanyId == companyId.Value && (isMainUser || u.CreatedByUserId == currentUserId))
                    .ToList();
            }

            // Final result
            var companyUsers = allUserList
                .OrderByDescending(u => u.IsMainUser)
                .ThenByDescending(u => u.UpdatedAt)
                .ToList();

            return Json(companyUsers);
        }


        [HttpPost]
        public async Task<IActionResult> AddFromCompany(User user)
        {
            if (!HasAccessRoles("Company_User", "Edit"))
            {
                return Forbid();
            }

            // Verify user can create users for this company
            if (!await CanAccessCompany(user.CompanyId))
            {
                return Forbid();
            }
            // 🔍 Check if username already exists
            var existingUser = await _userManager.FindByNameAsync(user.UserName);
            var userId = HttpContext.Session.GetInt32("UserId");

            if (existingUser != null)
            {
                return BadRequest(new
                {
                    success = false,
                    errors = new[] { _sharedLocalizer["usernametaken", user.UserName].Value }
                });
            }

            var newUser = new User
            {
                CompanyId = user.CompanyId,
                FullName = user.FullName,
                UserName = user.UserName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                IsMainUser = false,
                UserType = "CompanyUser",
                IsActive = true,
                CreatedAt = TimeHelper.NowInTurkey(),
                CreatedByUserId = userId
            };

            var result = await _userManager.CreateAsync(newUser, user.Password);

            if (!result.Succeeded)
            {
                var localizedErrors = result.Errors.Select(e =>
                      e.Code == "DuplicateUserName"
                          ? _sharedLocalizer["usernametaken", user.UserName].Value
                          : _sharedLocalizer[e.Description].Value
                );

                return BadRequest(new
                {
                    success = false,
                    errors = localizedErrors
                });
            }

            // ✅ Add role via Identity
            await _userManager.AddToRoleAsync(newUser, "CompanyUser");

            // ✅ Add to custom UserRoles table (if exists)
            _context.UserRoles.Add(new UserRole
            {
                UserId = newUser.Id,
                Name = "Company User",
                RoleId = 5 // CompanyUser Role ID
            });
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }
        [HttpGet]
        public async Task<IActionResult> GetUserById(int id)
        {
            if (!HasAccessRoles("Company_User", "Read"))
            {
                return Forbid();
            }

            // Check if user can access this specific user
            if (!await CanAccessUser(id))  
                return Forbid();

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            return Json(new
            {
                user.Id,
                user.FullName,
                user.UserName,
                user.Email,
                user.PhoneNumber,
                user.Quota,
                user.QuotaType,
                user.IsActive,
                VerificationType = user.VerificationType
            });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateUser([FromBody] User model)
        {
            if (!HasAccessRoles("Company_User", "Edit"))
            {
                return Forbid();
            }

            // Check if user can access this specific user
            if (!await CanEditUser(model.Id))
            {
                return Forbid();
            }
            var user = await _context.Users
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.Id == model.Id);

            if (user == null) return NotFound();

            user.CompanyId = model.CompanyId;
            user.FullName = model.FullName;
            user.UserName = model.UserName;
            user.Email = model.Email;
            user.PhoneNumber = model.PhoneNumber;
            user.VerificationType = model.VerificationType;


            if (user.IsMainUser != true)
            {
                user.QuotaType = model.QuotaType;
                user.Quota = model.Quota;
            }
            else
            {
                user.QuotaType = "Noquota";
                user.Quota = null;
            }
            bool passwordChanged = false;
            if (!string.IsNullOrWhiteSpace(model.Password))
            {
                user.PasswordHash = _userManager.PasswordHasher.HashPassword(user, model.Password);
                passwordChanged = true;
            }

            user.UpdatedAt = TimeHelper.NowInTurkey();

            // ✅ Add RoleId = 5 if not already present
            const int companyUserRoleId = 5;
            if (!user.UserRoles.Any(r => r.RoleId == companyUserRoleId))
            {
                var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleId == companyUserRoleId);
                if (role != null)
                {
                    _context.UserRoles.Add(new UserRole
                    {
                        UserId = user.Id,
                        RoleId = companyUserRoleId,
                        Name = role.RoleName
                    });
                }
            }

            await _context.SaveChangesAsync();
            if(passwordChanged)
            {
                int loggerUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
                var loggedinUser = await _context.Users            
                .FirstOrDefaultAsync(u => u.Id == loggerUserId);
                string loggedInUserName = loggedinUser?.UserName ?? "unknownUser";
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                var userAgent = Request.Headers["User-Agent"].ToString();
                var location = await SessionHelper.GetLocationFromIP(ipAddress);
                var textMsg = string.Format(
                                     _sharedLocalizer["userPwdChanged"], loggedInUserName,user.UserName, ipAddress, location, SessionHelper.ParseDevice(userAgent)
                                 );
                string dataJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Message = "User password updated into the system : " + user.UserName,
                    TelegramMessage = textMsg,
                    Time = TimeHelper.NowInTurkey(),
                    IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = Request.Headers["User-Agent"].ToString()
                });
                
                await _svc.UserLogAlertToAdmin(user.CompanyId ?? 0, user.Id, textMsg, dataJson);
            }
            return Json(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            // Check read permission (or edit for edit page)
            if (!HasAccessRoles("Company_User", "Edit"))
            {
                return Forbid();
            }

            // Check if user can access this specific user
            if (!await CanAccessUser(id))
            {
                return Forbid();
            }
            var user = await _context.Users
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.Id == id && u.CompanyId != null);

            var createdByUser = await _context.Users
            .Where(u => u.Id == user.CreatedByUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync();

            if (user == null)
                return NotFound();

            var currentUserId = HttpContext.Session.GetInt32("UserId");
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId);

            // Companies dropdown
            IQueryable<Company> companyQuery = _context.Companies;
            if (user.UserType == "CompanyUser")
            {
                companyQuery = companyQuery.Where(c => c.CompanyId == user.CompanyId);
            }
            ViewBag.IsEdit = true;
            ViewBag.Companies = await companyQuery
                .OrderBy(c => c.CompanyName)
                .Select(c => new SelectListItem
                {
                    Value = c.CompanyId.ToString(),
                    Text = c.CompanyName
                }).ToListAsync();

            // Load all assignable roles for this user
            IQueryable<Role> rolesQuery = _context.Roles;
            if (currentUser.UserType == "CompanyUser")
            {
                rolesQuery = rolesQuery.Where(r =>
                    r.RoleName == "Consumer" ||
                    r.RoleName == "Consumer_Admin" ||
                    (!r.IsGlobal && r.CreatedByCompanyId == currentUser.CompanyId));
            }
            else if (currentUser.UserType == "Admin")
            {
                rolesQuery = rolesQuery.Where(r => r.IsGlobal);
            }

            var allRoles = await rolesQuery.OrderBy(r => r.RoleName).ToListAsync();
            ViewBag.AllRoles = allRoles;

            // Selected role IDs
            var assignedRoleIds = user.UserRoles.Select(ur => ur.RoleId).ToList();
            ViewBag.AssignedRoleIds = assignedRoleIds;
            var visibleRoleIds = allRoles.Select(r => r.RoleId).ToList();
            // Load role permissions for each role
            var rolePermissions = await _context.RolePermissions
      .Where(rp => visibleRoleIds.Contains(rp.RoleId))
      .ToListAsync();

            // Group by RoleId for lookup in the view
            var rolePermissionDict = rolePermissions
                .GroupBy(rp => rp.RoleId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Build a view model
            var model = new UserEditViewModel
            {
                RolePermissions = rolePermissionDict,
                SelectedRoleIds = assignedRoleIds,
                AllRoles = allRoles,

                // Map user info
                Id = user.Id,
                FullName = user.FullName,
                UserName = user.UserName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                CompanyId = user.CompanyId,
                Quota = user.Quota,
                QuotaType = user.QuotaType,
                Password = user.Password,
                IsActive = user.IsActive,
                createdBy =  createdByUser ?? "-"
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(UserEditViewModel model)
        {
            // Check edit permission
            if (!HasAccessRoles("Company_User", "Edit"))
            {
                return Forbid();
            }

            // Check if user can access this specific user
            if (!await CanAccessUser(model.Id))
            {
                return Forbid();
            }
            var user = await _context.Users
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.Id == model.Id);

            if (user == null)
                return NotFound("User not found");

            // Update user info
            user.FullName = model.FullName;
            user.Email = model.Email;
            user.UserName = model.UserName;
            user.PhoneNumber = model.PhoneNumber;
            user.CompanyId = model.CompanyId;
            user.Quota = model.Quota;
            user.QuotaType = model.QuotaType;
            user.UpdatedAt = TimeHelper.NowInTurkey();
            bool passwordChanged = false;
            if (!string.IsNullOrWhiteSpace(model.Password))
            {
                user.PasswordHash = _userManager.PasswordHasher.HashPassword(user, model.Password);
                passwordChanged = true;
            }
            var existingRoleIds = user.UserRoles.Select(ur => ur.RoleId).ToList();
            var selectedRoleIds = model.SelectedRoleIds ?? new List<int>();

            var isCompanyUser = user.UserType == "CompanyUser";

            // Ensure RoleId 5 always exists for CompanyUsers
            if (isCompanyUser && !existingRoleIds.Contains(5))
            {
                existingRoleIds.Add(5);
            }
            if (isCompanyUser && !selectedRoleIds.Contains(5))
            {
                selectedRoleIds.Add(5);
            }

            // Determine what to remove and what to add
            var rolesToRemove = existingRoleIds.Except(selectedRoleIds).ToList();
            var rolesToAdd = selectedRoleIds.Except(existingRoleIds).ToList();

            // ✅ Remove roles only if needed (and not default company role)
            if (rolesToRemove.Any())
            {
                var toRemove = user.UserRoles
                    .Where(ur => rolesToRemove.Contains(ur.RoleId))
                    .ToList();

                _context.UserRoles.RemoveRange(toRemove);
            }

            // ✅ Add new roles without duplication
            foreach (var roleId in rolesToAdd)
            {
                if (!user.UserRoles.Any(r => r.RoleId == roleId)) // prevent duplicate
                {
                    var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId);
                    if (role != null)
                    {
                        _context.UserRoles.Add(new UserRole
                        {
                            UserId = user.Id,
                            RoleId = roleId,
                            Name = role.RoleName
                        });
                    }
                }
            }


            await _context.SaveChangesAsync();
            if (passwordChanged)
            {
                int loggerUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
                var loggedinUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == loggerUserId);
                string loggedInUserName = loggedinUser?.UserName ?? "unknownUser";
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                var userAgent = Request.Headers["User-Agent"].ToString();
                var location = await SessionHelper.GetLocationFromIP(ipAddress);
                var textMsg = string.Format(
                                     _sharedLocalizer["userPwdChanged"], loggedInUserName, user.UserName, ipAddress, location, SessionHelper.ParseDevice(userAgent)
                                 );
                string dataJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Message = "User password updated into the system : " + user.UserName,
                    TelegramMessage = textMsg
                });

                await _svc.UserLogAlertToAdmin(user.CompanyId ?? 0, user.Id, textMsg, dataJson);
            }
            HttpContext.Session.SetString("SessionVersion", Guid.NewGuid().ToString());
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleActiveUsers(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound();

            user.IsActive = !user.IsActive;
            user.UpdatedAt = TimeHelper.NowInTurkey(); // Optional
            await _context.SaveChangesAsync();

            return Json(new { isActive = user.IsActive });
        }

    }
}
