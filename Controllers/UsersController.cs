using GittBilSmsCore.Data;
using GittBilSmsCore.Helpers;
using GittBilSmsCore.Models;
using GittBilSmsCore.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using QRCoder;
using static QRCoder.QRCodeGenerator;

namespace GittBilSmsCore.Controllers
{
    public class UsersController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly UserManager<User> _userManager;
        public UsersController(GittBilSmsDbContext context, UserManager<User> userManager) : base(context)
        {
            _context = context;
            _userManager = userManager;
        }

        public IActionResult Index()
        {
            if (!HasAccessRoles("User", "Read"))
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var userType = HttpContext.Session.GetString("UserType") ?? "";
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            var isMainUser = HttpContext.Session.GetInt32("IsMainUser") == 1;

            var usersQuery = _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .AsQueryable();

            if (userType != "CompanyUser")
            {
                usersQuery = usersQuery.Where(u => u.CompanyId == null);
            }
            else
            {
                usersQuery = usersQuery.Where(u => u.CreatedByUserId == userId);
            }

            var users = usersQuery
                .OrderByDescending(u => u.CreatedAt)
                .ToList();

            var roles = _context.Roles
                .Where(r => r.IsGlobal && r.RoleName != "Company User")
                .ToList();

            var vm = new UserIndexViewModel
            {
                Users = users,
                Roles = roles
            };

            return View(vm);
        }
        public IActionResult Create()
        {
            var roles = _context.Roles.ToList();
            ViewBag.Roles = roles;
            return View(); // Form page
        }

        [HttpGet]
        public async Task<IActionResult> Enable2FA()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Account");

            // Load the user
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null)
                return NotFound();

            // If they’ve never had a 2FA secret, generate & save one.
            if (string.IsNullOrEmpty(user.TwoFactorSecretKey))
            {
                var key = KeyGeneration.GenerateRandomKey(20);
                var base32Secret = Base32Encoding.ToString(key);

                user.TwoFactorSecretKey = base32Secret;
                user.IsTwoFactorEnabled = true;
                await _userManager.UpdateAsync(user);
            }

            // Now use whatever is in user.TwoFactorSecretKey
            var secretToUse = user.TwoFactorSecretKey;
            var otpauthUrl = $"otpauth://totp/GittBilSms:{user.Email}?" +
                             $"secret={secretToUse}&issuer=GittBilSms";

            // Generate the QR code image
            var qrGen = new QRCodeGenerator();
            var qrData = qrGen.CreateQrCode(otpauthUrl, ECCLevel.Q);
            var qrCode = new Base64QRCode(qrData);
            var qrImage = qrCode.GetGraphic(10);

            // Build the view‐model
            var model = new Enable2FAViewModel
            {
                QrCodeImage = qrImage,
                SecretKey = secretToUse
            };

            return PartialView("_Enable2FA", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Enable2FA(string verificationCode, string secretKey)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Json(new { success = false, message = "You must be logged in." });

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return Json(new { success = false, message = "User not found." });

            var totp = new Totp(Base32Encoding.ToBytes(secretKey));
            bool isValid = totp.VerifyTotp(
                verificationCode,
                out _,
                VerificationWindow.RfcSpecifiedNetworkDelay
            );

            if (!isValid)
                return Json(new { success = false, message = "Invalid verification code." });

            user.TwoFactorSecretKey = secretKey;
            user.IsTwoFactorEnabled = true;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Two-Factor Authentication enabled successfully!" });
        }

        [HttpPost]
        public async Task<IActionResult> Create(User model, int[] selectedRoles)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Roles = _context.Roles.ToList();
                return View(model);
            }

            model.CreatedAt = TimeHelper.NowInTurkey();
            model.IsMainUser = false;
            model.UserType = "User";
            model.IsActive = true;
            model.CreatedByUserId = HttpContext.Session.GetInt32("UserId");

            var result = await _userManager.CreateAsync(model, model.Password);

            if (!result.Succeeded)
            {
                ViewBag.Roles = _context.Roles.ToList();
                foreach (var error in result.Errors)
                    ModelState.AddModelError("", error.Description);

                return View(model);
            }

            
            foreach (var roleId in selectedRoles)
            {
                var role = await _context.Roles.FindAsync(roleId);

                if (role != null)
                {
                    _context.UserRoles.Add(new UserRole
                    {
                        UserId = model.Id,
                        RoleId = roleId,
                        Name = role.RoleName 
                    });
                }
            }

            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        public IActionResult Details(int id)
        {
            var user = _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefault(u => u.Id == id);

            if (user == null)
                return NotFound();

            // ✅ Only include roles that are global and not "Company User"
            var roles = _context.Roles
                .Where(r => r.IsGlobal && r.RoleName != "Company User")
                .ToList();

            ViewBag.AllRoles = roles;

            var rolePermissions = _context.RolePermissions
                .Where(rp => roles.Select(r => r.RoleId).Contains(rp.RoleId))
                .GroupBy(rp => rp.RoleId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var model = new UserDetailsViewModel
            {
                User = user,
                Roles = user.UserRoles.Select(ur => ur.Role).ToList(),
                RolePermissions = rolePermissions
            };

            return View(model);
        }


        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _context.Users
       .Include(u => u.UserRoles) // include related roles
       .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound();

            // ✅ Remove related UserRoles first
            _context.UserRoles.RemoveRange(user.UserRoles);

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "User deleted successfully.";
            return RedirectToAction("Index");
        }
        [HttpPost]
        public async Task<IActionResult> UpdateProfile(ProfileViewModel model)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            // Update basic fields
            user.FullName = model.FullName;
            user.Email = model.Email;
            user.PhoneNumber = model.PhoneNumber;

            var method = model.PreferredTwoFactorMethod;
            if (!Enum.IsDefined(typeof(TwoFactorMethod), method))
            {
                method = TwoFactorMethod.None;
            }
            user.PreferredTwoFactorMethod = method;

            // Disable 2FA when “None”
            user.IsTwoFactorEnabled = (method != TwoFactorMethod.None);

            // Password change (if provided)
            if (!string.IsNullOrEmpty(model.NewPassword))
            {
                // It's better to use UserManager to hash & set the password,
                // but if you're rolling your own:
                user.PasswordHash = _userManager.PasswordHasher.HashPassword(user, model.NewPassword);
            }

            // Profile photo upload
            if (model.ProfilePhoto != null && model.ProfilePhoto.Length > 0)
            {
                var uploadsFolder = Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot/uploads");
                if (!System.IO.Directory.Exists(uploadsFolder))
                    System.IO.Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(model.ProfilePhoto.FileName);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using var fileStream = new FileStream(filePath, FileMode.Create);
                await model.ProfilePhoto.CopyToAsync(fileStream);

                user.ProfilePhotoUrl = "/uploads/" + uniqueFileName;
            }
            // If no new file, we leave user.ProfilePhotoUrl as-is

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Profile updated successfully!" });
        }
        public async Task<IActionResult> Profile()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return RedirectToAction("Login", "Account");

            var user = await _context.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound();

            var setTelegramId = "";

            if (user.TelegramUserId != null && user.TelegramUserId > 0)
            {
                setTelegramId = "Telegram Linked";
            }
            else
            {
                setTelegramId = "https://t.me/gittibiladmin_bot?start=" + Uri.EscapeDataString(userId.ToString());
            }

            var model = new ProfileViewModel
            {
                FullName = user.FullName,
                Username = user.UserName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                IsTwoFactorEnabled = user.IsTwoFactorEnabled,
                ExistingProfilePhotoUrl = user.ProfilePhotoUrl, // <-- this must be correctly mapped
                Roles = user.UserRoles.Select(ur => ur.Name).ToList(),
                PreferredTwoFactorMethod = user.PreferredTwoFactorMethod,
                TelegramUserId = user.TelegramUserId,
                BindTelegramId = setTelegramId
            };

            return View(model);
        }

        [HttpPost]
        public IActionResult Details(User model, int[] selectedRoles)
        {
            var user = _context.Users
                .Include(u => u.UserRoles)
                .FirstOrDefault(u => u.Id == model.Id);

            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction("Index");
            }

            try
            {
                // Update user fields
                user.FullName = model.FullName;
                user.UserName = model.UserName;
                user.Email = model.Email;
                user.PhoneNumber = model.PhoneNumber;
                user.VerificationType = "";
                user.UserType = "";
               // user.Password = model.Password; 
                user.UpdatedAt = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    user.PasswordHash = _userManager.PasswordHasher.HashPassword(user, model.Password);
                }
                // Update roles
                _context.UserRoles.RemoveRange(user.UserRoles);
                var roles = _context.Roles
                .Where(r => selectedRoles.Contains(r.RoleId))
                .ToDictionary(r => r.RoleId, r => r.RoleName);
                foreach (var roleId in selectedRoles)
                {
                    _context.UserRoles.Add(new UserRole
                    {
                        UserId = user.Id,
                        RoleId = roleId,
                        Name = roles.ContainsKey(roleId) ? roles[roleId] : null
                    });
                }

                _context.SaveChanges();

                TempData["SuccessMessage"] = "User updated successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "An error occurred while updating the user.";
                // Log the exception (optional): _logger.LogError(ex, "User update failed.");
            }

            return RedirectToAction("Details", new { id = user.Id });

        }
    }

}
