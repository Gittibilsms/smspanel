using GittBilSmsCore.Data;
using GittBilSmsCore.Helpers;
using GittBilSmsCore.Models;
using GittBilSmsCore.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GittBilSmsCore.Controllers
{
    public class RolesController : BaseController
    {
        private readonly GittBilSmsDbContext _context;

        public RolesController(GittBilSmsDbContext context) : base(context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
       
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Account");

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
                return RedirectToAction("Login", "Account");

            IQueryable<Role> rolesQuery = _context.Roles;

            if (user.UserType == "CompanyUser")
            {
                rolesQuery = rolesQuery.Where(r =>
                    (r.RoleName == "Consumer" || r.RoleName == "Consumer_Admin" || (!r.IsGlobal && r.CreatedByCompanyId == user.CompanyId)) &&
                    r.RoleName != "Company User"
                );
            }
            else if (user.UserType == "Admin")
            {
                rolesQuery = rolesQuery.Where(r => r.IsGlobal && r.RoleName != "Company User"); // ✅ Exclude here
            }
            else
            {
                rolesQuery = rolesQuery.Where(r => r.RoleName != "Company User"); // ✅ Exclude here too
            }

            var roles = rolesQuery.OrderBy(r => r.RoleName).ToList();
            return View(roles);
        }

        // GET: /Roles/Create
        public IActionResult Create()
        {
            //if (!HasPermission("Roles_Create"))
            //{
            //    return RedirectToAction("AccessDenied", "Account");
            //}

            var model = new RolePermissionViewModel
            {
                RoleName = "",
                UserPermissions = GetDefaultUserPermissions(),
                DealerPermissions = GetDefaultDealerPermissions(),
                ConnectUserAccount = false,
                RoleAssignmentAuthority = false
            };
            return View(model);
        }

        // POST: /Roles/Create
        [HttpPost]
        public async Task<IActionResult> Create(RolePermissionViewModel model)
        {
            // if (!HasPermission("Roles_Create")) return RedirectToAction("AccessDenied", "Account");

            if (!ModelState.IsValid)
            {
                model.UserPermissions = GetDefaultUserPermissions();
                model.DealerPermissions = GetDefaultDealerPermissions();
                return View(model);
            }

            // Get logged in user
            var userId = HttpContext.Session.GetInt32("UserId");
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            // Save Role
            var role = new Role
            {
                RoleName = model.RoleName,
                IsReadOnly = model.IsReadOnly,
                CreatedAt = TimeHelper.NowInTurkey(),
                UpdatedAt = TimeHelper.NowInTurkey(),

                // ✅ New logic
                IsGlobal = user.UserType != "CompanyUser",
                CreatedByCompanyId = user.UserType == "CompanyUser" ? user.CompanyId : null
            };

            _context.Roles.Add(role);
            await _context.SaveChangesAsync();

            // Save Permissions
            var allModules = new[]
            {
        "Firm", "Company_User", "Order", "User", "Directory", "Blacklist", "Passive_numbers",
        "Credit", "Request_for_support", "Account_transactions", "Dealer_transactions",
        "Dealer", "DealerCredit", "DealerSupportRequest", "DealerAccountTransactions", "DealerUsers"
    };

            foreach (var module in allModules)
            {
                var type = module.StartsWith("Dealer") ? "Dealer" : "User";

                _context.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.RoleId,
                    Module = module,
                    Type = type,
                    CanRead = model.IsReadOnly || model.SelectedPermissions.Contains($"{module}_Read"),
                    CanCreate = model.SelectedPermissions.Contains($"{module}_Create"),
                    CanEdit = model.SelectedPermissions.Contains($"{module}_Edit"),
                    CanDelete = model.SelectedPermissions.Contains($"{module}_Delete")
                });
            }

            if (model.ConnectUserAccount)
            {
                _context.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.RoleId,
                    Module = "ConnectUser",
                    Special = true,
                    Type = "Special"
                });
            }

            if (model.RoleAssignmentAuthority)
            {
                _context.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.RoleId,
                    Module = "AssignRole",
                    Special = true,
                    Type = "Special"
                });
            }

            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        // GET: /Roles/Permissions/5
        public async Task<IActionResult> Permissions(int roleId)
        {
            //if (!HasPermission("Roles_Edit"))
            //{
            //    return RedirectToAction("AccessDenied", "Account");
            //}

            var role = await _context.Roles.FindAsync(roleId);
            if (role == null) return NotFound();

            var permissions = await _context.RolePermissions
                .Where(p => p.RoleId == roleId)
                .ToListAsync();

            var selectedPermissions = permissions
                .SelectMany(p => new[]
                {
                p.CanRead ? $"{p.Module}_Read" : null,
                p.CanCreate ? $"{p.Module}_Create" : null,
                p.CanEdit ? $"{p.Module}_Edit" : null,
                p.CanDelete ? $"{p.Module}_Delete" : null
                })
                .Where(p => p != null)
                .ToList();
            selectedPermissions = selectedPermissions
                .Where(p => !p.StartsWith("User_"))
                .Distinct()
                .ToList();
            var model = new RolePermissionViewModel
            {
                RoleId = role.RoleId,
                RoleName = role.RoleName,
                IsReadOnly = role.IsReadOnly,
                ConnectUserAccount = permissions.Any(p => p.Module == "ConnectUser" && p.Special),
                RoleAssignmentAuthority = permissions.Any(p => p.Module == "AssignRole" && p.Special),
                SelectedPermissions = selectedPermissions
            };

            return View(model);
        }

        // POST: /Roles/Permissions/5
        [HttpPost]
        public async Task<IActionResult> Permissions(RolePermissionViewModel model)
        {
            var role = await _context.Roles.FindAsync(model.RoleId);
            if (role == null)
                return NotFound();

            // Clear old permissions
            var oldPermissions = _context.RolePermissions.Where(p => p.RoleId == model.RoleId);
            _context.RolePermissions.RemoveRange(oldPermissions);

            var allModules = new[]
            {
                "Firm", "User", "Order", "Directory", "Blacklist", "Passive_numbers",
                "Credit", "Request_for_support", "Account_transactions", "Dealer_transactions",
                "Dealer", "DealerCredit", "DealerSupportRequest", "DealerAccountTransactions", "DealerUsers"
            };

            var addedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var module in allModules)
            {
                if (addedModules.Contains(module))
                    continue;

                var type = module.StartsWith("Dealer") ? "Dealer" : "User";

                bool canRead = model.SelectedPermissions.Contains($"{module}_Read");
                bool canCreate = model.SelectedPermissions.Contains($"{module}_Create");
                bool canEdit = model.SelectedPermissions.Contains($"{module}_Edit");
                bool canDelete = model.SelectedPermissions.Contains($"{module}_Delete");

                _context.RolePermissions.Add(new RolePermission
                {
                    RoleId = model.RoleId,
                    Module = module,
                    Type = type,
                    CanRead = canRead,
                    CanCreate = canCreate,
                    CanEdit = canEdit,
                    CanDelete = canDelete
                });

                addedModules.Add(module);

                // Mirror to "User" if current module is "Company_User"
                if (module == "User")
                {
                    _context.RolePermissions.Add(new RolePermission
                    {
                        RoleId = model.RoleId,
                        Module = "Company_User",
                        Type = "User",
                        CanRead = canRead,
                        CanCreate = canCreate,
                        CanEdit = canEdit,
                        CanDelete = canDelete
                    });

                    addedModules.Add("User");
                }
            }

            // ✅ These must be INSIDE the method
            if (model.ConnectUserAccount)
            {
                _context.RolePermissions.Add(new RolePermission
                {
                    RoleId = model.RoleId,
                    Module = "ConnectUser",
                    Type = "Special",
                    Special = true
                });
            }

            if (model.RoleAssignmentAuthority)
            {
                _context.RolePermissions.Add(new RolePermission
                {
                    RoleId = model.RoleId,
                    Module = "AssignRole",
                    Type = "Special",
                    Special = true
                });
            }

            await _context.SaveChangesAsync();
            HttpContext.Session.SetString("SessionVersion", Guid.NewGuid().ToString());
            return RedirectToAction("Index");
        }


        [HttpPost]
        public async Task<IActionResult> Delete(int roleId)
        {
            var role = await _context.Roles.FindAsync(roleId);
            if (role == null) return NotFound();

            var permissions = _context.RolePermissions.Where(p => p.RoleId == roleId);
            _context.RolePermissions.RemoveRange(permissions);

            _context.Roles.Remove(role);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        private List<PermissionItem> GetDefaultUserPermissions()
        {
            return new List<PermissionItem>
        {
            new PermissionItem { ModuleName = "Users" },
            new PermissionItem { ModuleName = "Orders" },
            new PermissionItem { ModuleName = "Companies" }
        };
        }

        private List<PermissionItem> GetDefaultDealerPermissions()
        {
            return new List<PermissionItem>
        {
            new PermissionItem { ModuleName = "Dealers" },
            new PermissionItem { ModuleName = "Credit" }
        };
        }
    }
}
