using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GittBilSmsCore.Helpers;
using System.Text.Json;
using GittBilSmsCore.Services;
using GittBilSmsCore.Enums;
using DocumentFormat.OpenXml.InkML;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Identity;
using System.Text.Json.Serialization;
using OtpNet;
using Microsoft.AspNetCore.Authorization;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json;

namespace GittBilSmsCore.Controllers
{
    public class AccountController : BaseController
    {
        private readonly GittBilSmsDbContext _context;
        private readonly IStringLocalizer _sharedLocalizer;
        private readonly INotificationService _notificationService;
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly IEmailSender _emailSender;
        private readonly TelegramMessageService _svc;
        public AccountController(
        GittBilSmsDbContext context,
        IStringLocalizerFactory factory,
        INotificationService notificationService,
        UserManager<User> userManager,
        SignInManager<User> signInManager,
          IEmailSender emailSender, TelegramMessageService svc
    ) : base(context)
        {
            _context = context;
            _sharedLocalizer = factory.Create("SharedResource", "GittBilSmsCore");
            _notificationService = notificationService;
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _svc = svc;
        }
        public IActionResult Index()
        {
            return View();
        }
        [HttpGet]
        public IActionResult Login() => View();

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
        [HttpPost, AllowAnonymous]
        public async Task<IActionResult> Login(string username, string password)
        {
            // 1) Lookup & basic existence check
            var user = await _userManager.Users
                 .Include(u => u.Company)
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                .FirstOrDefaultAsync(u => u.UserName == username);
            if(user != null)
            {
                if (!user.IsActive)
                {
                    await LogUnauthorized(username);
                    return BadRequest(new { value = _sharedLocalizer["userinactive"].Value });
                }
            }
     
            if (user == null)
            {
                await LogUnauthorized(username);
                return BadRequest(new { value = _sharedLocalizer["invalidcredentials"].Value });
            }
            if (user.Company != null && user.Company.IsActive == false)
            {
                await LogUnauthorized(username);
                return BadRequest(new { value = _sharedLocalizer["companyinactive"].Value });
            }
        
            // 2) Verify password (but don’t sign in yet)
            var pwCheck = await _signInManager.CheckPasswordSignInAsync(
                user, password, lockoutOnFailure: true);

            if (!pwCheck.Succeeded)
            {
                await LogUnauthorized(username);
                return BadRequest(new { value = _sharedLocalizer["invalidcredentials"].Value });
            }

            // 3) Only challenge 2FA if the flag is on
            if (user.IsTwoFactorEnabled)
            {
                switch (user.PreferredTwoFactorMethod)
                {
                    case TwoFactorMethod.Authenticator:
                        // TOTP app flow (ensure they actually have a secret)
                        if (string.IsNullOrEmpty(user.TwoFactorSecretKey))
                        {
                            // fallback: treat as no-2FA
                            break;
                        }

                        HttpContext.Session.SetInt32("Pending2FAUserId", user.Id);
                        return Json(new { require2FA = true });

                    case TwoFactorMethod.Email:
                        // Email OTP flow
                        if (string.IsNullOrWhiteSpace(user.Email))
                        {
                            // log, notify, or choose a fallback (e.g. TOTP or error out)
                            Console.WriteLine($"⚠️ User {username} requested email 2FA but has no email.");
                            break;
                        }

                        var token = await _userManager.GenerateTwoFactorTokenAsync(
                            user, TokenOptions.DefaultEmailProvider);

                        await _emailSender.SendEmailAsync(
                            user.Email,
                            "Giriş Doğrulama Kodu",
                            $"<p>Giriş kodunuz: <strong>{token}</strong></p>");

                        HttpContext.Session.SetInt32("Pending2FAUserId", user.Id);
                        return Json(new { requireEmail2FA = true });

                    default:
                        // They’ve turned on 2FA but have no valid provider chosen => fall back
                        break;
                }
            }

            // 4) No 2FA required, or fallback: complete the sign-in
            await _signInManager.SignInAsync(user, isPersistent: false);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = Request.Headers["User-Agent"].ToString();
            var location = await GetLocationFromIP(ipAddress);
            _context.LoginHistories.Add(new LoginHistory
            {
                UserId = user.Id,
                Username = user.UserName,
                IPAddress = ipAddress,
                UserAgent = userAgent,
                Device = ParseDevice(userAgent),
                Location = location,
                LoggedInAt = TimeHelper.NowInTurkey()
            });
            await _context.SaveChangesAsync();
            var textMsg = string.Format(
                                      _sharedLocalizer["userloggedMessage"] ,user.UserName, ipAddress, location, ParseDevice(userAgent)
                                  );
            string dataJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                Message = "User logged into the system : " + user.UserName,
                TelegramMessage = textMsg,
                Time = TimeHelper.NowInTurkey(),
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString()
            });
            SetUserSession(user);
            var userType = HttpContext.Session.GetString("UserType") ?? string.Empty;

            if (!userType.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            {
                await _svc.UserLogAlertToAdmin(user.CompanyId ?? 0, user.Id, textMsg, dataJson);
            }


            Console.WriteLine($"✅ User '{user.UserName}' logged in successfully.");
            return Ok();
        }
        public string ParseDevice(string userAgent)
        {
            if (userAgent.Contains("Windows")) return "Windows";
            if (userAgent.Contains("Android")) return "Android";
            if (userAgent.Contains("iPhone") || userAgent.Contains("iPad")) return "iOS";
            if (userAgent.Contains("Macintosh")) return "Mac";
            return "Unknown";
        }

        public async Task<string> GetLocationFromIP(string ip)
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetStringAsync($"http://ip-api.com/json/{ip}");
            dynamic data = JsonConvert.DeserializeObject(response);
            return $"{data.city}, {data.country}";
        }
        [HttpGet, AllowAnonymous]
        public IActionResult VerifyEmail2FA()
        {
            if (HttpContext.Session.GetInt32("Pending2FAUserId") == null)
                return RedirectToAction("Login");
            return View();
        }
        [HttpPost, AllowAnonymous]
        public async Task<IActionResult> VerifyEmail2FA(string code)
        {
            var userId = HttpContext.Session.GetInt32("Pending2FAUserId");
            if (userId == null) return RedirectToAction("Login");

            var user = await _userManager.Users
              .Include(u => u.UserRoles)
                  .ThenInclude(ur => ur.Role)
                      .ThenInclude(r => r.RolePermissions)
              .FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login");

            var valid = await _userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultEmailProvider, code);

            if (!valid)
            {
                ViewBag.TwoFactorError = "Kod geçersiz. Lütfen tekrar deneyin.";
                return View();
            }

            // 1) Sign in the cookie
            await _signInManager.SignInAsync(user, isPersistent: false);

            // 2) Populate your session exactly like in the TOTP flow
            SetUserSession(user);

            // 3) Clean up the pending flag
            HttpContext.Session.Remove("Pending2FAUserId");

            // 4) Now your BaseController will see UserId in session and allow /Home
            return RedirectToAction("Index", "Home");
        }
        private void SetUserSession(User user)
        {
            HttpContext.Session.SetInt32("UserId", user.Id);
         //   HttpContext.Session.SetString("UserType", user.UserType ?? "Unknown");
            HttpContext.Session.SetInt32("IsMainUser", (user.IsMainUser ?? false) ? 1 : 0);
            var roleName = user.UserRoles?
    .Select(ur => ur.Role?.RoleName)
    .FirstOrDefault() ?? "User"; // Default fallback
            if (roleName.Equals("Company User", StringComparison.OrdinalIgnoreCase))
            {
                roleName = "CompanyUser";
            }
            HttpContext.Session.SetString("UserType", roleName);
            if (user.CompanyId.HasValue)
            {
                HttpContext.Session.SetInt32("CompanyId", user.CompanyId.Value);

                // ✅ Only set CanSendSupportRequest if company is loaded
                if (user.Company != null)
                {
                    HttpContext.Session.SetInt32("CanSendSupportRequest", user.Company.CanSendSupportRequest ? 1 : 0);
                    var isMainUser = (user.IsMainUser ?? false);
                    var availableCredit = isMainUser
                        ? (user.Company?.CreditLimit ?? 0)
                        : (user.Quota ?? 0);

                    HttpContext.Session.SetString("AvailableCredit",
                        availableCredit.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

                }
                else
                {
                    HttpContext.Session.SetInt32("CanSendSupportRequest", 0); // default fallback
                }
            }

            var roleIds = user.UserRoles.Select(ur => ur.RoleId).ToList();
            if (roleIds.Any())
            {
                HttpContext.Session.SetString("RoleIds", string.Join(",", roleIds));
                HttpContext.Session.SetInt32("RoleId", roleIds.First());

                var permissions = user.UserRoles?
                    .Where(ur => ur.Role?.RolePermissions != null)
                    .SelectMany(ur => ur.Role.RolePermissions)
                    .Select(p => new RolePermission
                    {
                        RoleId = p.RoleId,
                        Module = p.Module,
                        Type = p.Type,
                        Special = p.Special,
                        CanRead = p.CanRead,
                        CanCreate = p.CanCreate,
                        CanEdit = p.CanEdit,
                        CanDelete = p.CanDelete
                    })
                    .ToList();

                var permissionJson = System.Text.Json.JsonSerializer.Serialize(permissions);
                HttpContext.Session.SetString("UserPermissions", permissionJson);
            }
        }
        [HttpPost]
        public async Task<IActionResult> RefreshSession()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized();

            var user = await _context.Users
                 .Include(u => u.Company)
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                        .ThenInclude(r => r.RolePermissions)
                .FirstOrDefaultAsync(u => u.Id == userId.Value);
            if (user == null) return Json(new { success = false });

            // 1) Repopulate _all_ your Session[...] values
            SetUserSession(user);

            // 2) Then handle the “version” GUID
            var ver = HttpContext.Session.GetString("SessionVersion");
            if (string.IsNullOrEmpty(ver))
            {
                ver = Guid.NewGuid().ToString();
                HttpContext.Session.SetString("SessionVersion", ver);
            }
            return Json(new { success = true, version = ver });
        }
        [HttpGet]
        public IActionResult Verify2FA()
        {
            var pendingUserId = HttpContext.Session.GetInt32("Pending2FAUserId");
            if (pendingUserId == null)
            {
                // If no pending 2FA user, redirect to login
                return RedirectToAction("Login");
            }

            return View(); // Shows the input for 6-digit TOTP code
        }
        [HttpPost, AllowAnonymous]
        public async Task<IActionResult> Verify2FA(string verificationCode)
        {
            var pendingUserId = HttpContext.Session.GetInt32("Pending2FAUserId");
            if (pendingUserId == null)
                return RedirectToAction("Login");

            // now TwoFactorSecretKey in the DB is exactly the one you generated
            var user = await _userManager.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                        .ThenInclude(r => r.RolePermissions)
                .FirstOrDefaultAsync(u => u.Id == pendingUserId);

            var totp = new Totp(Base32Encoding.ToBytes(user.TwoFactorSecretKey));
            if (!totp.VerifyTotp(verificationCode, out _, VerificationWindow.RfcSpecifiedNetworkDelay))
            {
                ViewBag.TwoFactorError = "Kod geçersiz. Lütfen tekrar deneyin.";
                return View();
            }

            SetUserSession(user);
            HttpContext.Session.Remove("Pending2FAUserId");
            return RedirectToAction("Index", "Home");
        }
        private async Task LogUnauthorized(string username)
        {
            await _notificationService.AddNotificationAsync(new Notifications
            {
                Title = _sharedLocalizer["unauthorizedlogin"],
                Description = _sharedLocalizer["invalidloginattempt", username],
                Type = NotificationType.UnauthorizedLogin,
                CreatedAt = TimeHelper.NowInTurkey(),
                IsRead = false,
                CompanyId = null
            });

            Console.WriteLine("❌ Login failed: Invalid credentials.");
        }

        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "Account");
        }
    }
}
