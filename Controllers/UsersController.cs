using GittBilSmsCore.Data;
using GittBilSmsCore.Helpers;
using GittBilSmsCore.Models;
using GittBilSmsCore.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using OtpNet;
using QRCoder;
using static QRCoder.QRCodeGenerator;

namespace GittBilSmsCore.Controllers
{
    public class UsersController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly IStringLocalizer _sharedLocalizer;
        private readonly TelegramMessageService _svc;
        public UsersController(GittBilSmsDbContext context, UserManager<User> userManager, IStringLocalizerFactory factory, TelegramMessageService svc) : base(context)
        {
            _context = context;
            _userManager = userManager;
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
            _svc = svc;
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

        [HttpGet]
        public IActionResult Create()
        {
            var vm = new UserIndexViewModel
            {
                NewUser = new User(),
                Roles = _context.Roles.ToList()
            };

            return View(vm);
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(User model, int[] selectedRoles)
        {
            if (selectedRoles == null || selectedRoles.Length == 0)
                ModelState.AddModelError("selectedRoles", "At least one role must be selected.");

            if (!ModelState.IsValid)
            {
                var vmErr = new UserIndexViewModel
                {
                    NewUser = model,
                    Roles = _context.Roles.ToList()
                };
                ViewBag.ToastrType = "error";
                ViewBag.ToastrTitle = "Validation failed";
                ViewBag.ToastrMessage = string.Join("<br/>",
                    ModelState.Where(x => x.Value?.Errors.Count > 0)
                              .SelectMany(x => x.Value.Errors.Select(e => e.ErrorMessage)));
                return View(vmErr); 
            }

            model.CreatedAt = TimeHelper.NowInTurkey();
            model.IsMainUser = false;
            model.UserType = "User";
            model.IsActive = true;
            model.CreatedByUserId = HttpContext.Session.GetInt32("UserId");

            var result = await _userManager.CreateAsync(model, model.Password);
            if (!result.Succeeded)
            {
                var vmErr = new UserIndexViewModel
                {
                    NewUser = model,
                    Roles = _context.Roles.ToList()
                };
                ViewBag.ToastrType = "error";
                ViewBag.ToastrTitle = "Error";
                ViewBag.ToastrMessage = string.Join("<br/>", result.Errors.Select(e => e.Description));
                return View(vmErr);  
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

            // send alert to admin about new user creation
            int loggerUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
            var loggedinUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == loggerUserId);
            string loggedInUserName = loggedinUser?.UserName ?? "unknownUser";
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = Request.Headers["User-Agent"].ToString();
            var location = await SessionHelper.GetLocationFromIP(ipAddress);
            var textMsg = string.Format(
                                 _sharedLocalizer["userCreated"], loggedInUserName, model.UserName, ipAddress, location, SessionHelper.ParseDevice(userAgent)
                             );
            string dataJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                Message = "New User Created:" + model.UserName,
                TelegramMessage = textMsg,
                Time = TimeHelper.NowInTurkey(),
                IPAddress = ipAddress,
                UserAgent = userAgent
            });

            await _svc.UserLogAlertToAdmin(0, 0, textMsg, dataJson);

            var vmOk = new UserIndexViewModel
            {
                NewUser = new User(),               // optional: clear the form
                Roles = _context.Roles.ToList()
            };
            ViewBag.ToastrType = "success";
            ViewBag.ToastrTitle = "Success";
            ViewBag.ToastrMessage = "User created successfully.";
            ViewBag.RedirectUrl = Url.Action("Index"); // used by JS for redirect
            ViewBag.RedirectDelay = 3000;                // ms

            return View("Create", vmOk);
        }
        [HttpGet]
        public IActionResult Details(int id)
        {
            var user = _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefault(u => u.Id == id);

            if (user == null)
            {
                TempData["ToastrType"] = "error";
                TempData["ToastrTitle"] = "Error";
                TempData["ToastrMessage"] = "User not found.";
                return RedirectToAction("Index");
            }

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
            ViewBag.ToastrType = TempData["ToastrType"];
            ViewBag.ToastrTitle = TempData["ToastrTitle"];
            ViewBag.ToastrMessage = TempData["ToastrMessage"];
            ViewBag.RedirectUrl = TempData["RedirectUrl"];
            ViewBag.RedirectDelay = TempData["RedirectDelay"];
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

            bool passwordChanged = false;
            // Password change (if provided)
            if (!string.IsNullOrEmpty(model.NewPassword))
            {              
                user.PasswordHash = _userManager.PasswordHasher.HashPassword(user, model.NewPassword);
                passwordChanged = true;
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
                    TelegramMessage = textMsg,
                    Time = TimeHelper.NowInTurkey(),
                    IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = Request.Headers["User-Agent"].ToString()
                });

                await _svc.UserLogAlertToAdmin(user.CompanyId ?? 0, user.Id, textMsg, dataJson);
            }
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
            
             
           var  setTelegramId = "https://t.me/gittibiladmin_bot?start=" + Uri.EscapeDataString(userId.ToString());
            
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Details(User model, int[] selectedRoles)
        {
            ModelState.Remove(nameof(model.Password));     
            ModelState.Remove("model.Password");

            if (selectedRoles == null || selectedRoles.Length == 0)
                ModelState.AddModelError("selectedRoles", "At least one role must be selected.");
            if (!ModelState.IsValid)
            {
                // Rebuild the same data as GET for redisplay
                var userFromDb = _context.Users
                    .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                    .FirstOrDefault(u => u.Id == model.Id);

                if (userFromDb == null)
                {
                    TempData["ToastrType"] = "error";
                    TempData["ToastrTitle"] = "Error";
                    TempData["ToastrMessage"] = "User not found.";
                    return RedirectToAction("Index");
                }

                var roles = _context.Roles
                    .Where(r => r.IsGlobal && r.RoleName != "Company User")
                    .ToList();
                ViewBag.AllRoles = roles;

                var rolePermissions = _context.RolePermissions
                    .Where(rp => roles.Select(r => r.RoleId).Contains(rp.RoleId))
                    .GroupBy(rp => rp.RoleId)
                    .ToDictionary(g => g.Key, g => g.ToList());
               
                var vm = new UserDetailsViewModel
                {
                    User = userFromDb, 
                    Roles = userFromDb.UserRoles.Select(ur => ur.Role).ToList(),
                    RolePermissions = rolePermissions
                };
               
                vm.User.FullName = model.FullName;
                vm.User.UserName = model.UserName;
                vm.User.Email = model.Email;
                vm.User.PhoneNumber = model.PhoneNumber;   
                ViewBag.ToastrType = "error";
                ViewBag.ToastrTitle = "Validation failed";
                ViewBag.ToastrMessage = string.Join("<br/>",
                    ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));

                return View(vm); 
            }

            var user = _context.Users.Include(u => u.UserRoles).FirstOrDefault(u => u.Id == model.Id);

            if (user == null)
            {
                TempData["ToastrType"] = "error";
                TempData["ToastrTitle"] = "Error";
                TempData["ToastrMessage"] = "User not found.";
                return RedirectToAction("Index");
            }

            try
            {
                 
                user.FullName = model.FullName;
                user.UserName = model.UserName;
                user.Email = model.Email;
                user.PhoneNumber = model.PhoneNumber;
                user.VerificationType = "";
                user.UserType = "";              
                user.UpdatedAt = DateTime.UtcNow;
                bool ispwdChanged = false;
                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    user.PasswordHash = _userManager.PasswordHasher.HashPassword(user, model.Password);
                    ispwdChanged = true;
                }               
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
                if (ispwdChanged) 
                {
                    int loggerUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
                    var loggedinUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == loggerUserId);
                    string loggedInUserName = loggedinUser?.UserName ?? "unknownUser";
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    var userAgent = Request.Headers["User-Agent"].ToString();
                    var location = await SessionHelper.GetLocationFromIP(ipAddress);
                    var textMsg = string.Format(
                                         _sharedLocalizer["userPwdChanged"], loggedInUserName, model.UserName, ipAddress, location, SessionHelper.ParseDevice(userAgent)
                                     );
                    string dataJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Message = "Password changed :" + model.UserName,                        
                        TelegramMessage = textMsg,
                        Time = TimeHelper.NowInTurkey(),
                        IPAddress = ipAddress,
                        UserAgent = userAgent
                    });

                    await _svc.UserLogAlertToAdmin(0, 0, textMsg, dataJson);

                }

                TempData["ToastrType"] = "success";
                TempData["ToastrTitle"] = "Success";
                TempData["ToastrMessage"] = "User updated successfully.";
                TempData["RedirectUrl"] = Url.Action("Index", "Users");
                TempData["RedirectDelay"] = 3000; // ms                 
            }
            catch (Exception ex)
            {
                TempData["ToastrType"] = "error";
                TempData["ToastrTitle"] = "Error";
                TempData["ToastrMessage"] = "An error occurred while updating the user.";
            }

            return RedirectToAction("Details", new { id = user.Id });

        }
    }

}
