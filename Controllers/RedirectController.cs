using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UAParser;

namespace GittBilSmsCore.Controllers
{
    public class RedirectController : Controller
    {
        private readonly GittBilSmsDbContext _context;

        // ✅ FIXED: Match the encoding order from controllers!
        private const string Base62Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        public RedirectController(GittBilSmsDbContext context)
        {
            _context = context;
        }

        // ✅ FIXED: Removed /s/ from route
       //[Route("{shortCode}/{token?}")]
        public async Task<IActionResult> RedirectToOriginal(string shortCode, string token = null)
        {
            if (!Request.Host.Host.Equals("l.go2s.me", StringComparison.OrdinalIgnoreCase))
                return NotFound();

            Console.WriteLine($"[RedirectController] Hit! Host: {Request.Host.Host}, Path: {Request.Path}, ShortCode: {shortCode}");

            // ✅ CRITICAL FIX: Only process short URLs on l.go2s.me domain
            var host = Request.Host.Host.ToLower();
            if (!host.Equals("l.go2s.me", StringComparison.OrdinalIgnoreCase))
            {
                // Not on short URL domain - let other controllers handle it
                Console.WriteLine($"[RedirectController] Not on l.go2s.me domain ({host}) - returning NotFound to let other controllers handle");
                return NotFound();
            }

            // ✅ If on l.go2s.me and trying to access controller pages, redirect to portal
            var controllerNames = new[] {
                "account", "admin", "api", "base", "blacklist", "companies", "companyusers",
                "credit", "credittransactions", "dealerdashboard", "directory", "downloads",
                "health", "home", "notifications", "orderexport", "orders", "payments",
                "pricing", "redirect", "report", "roles", "shorturls", "smsapi", "support",
                "transactions", "userinfo", "users", "swagger", "login", "logout", "register",
                "forgotpassword", "resetpassword", "dashboard", "tickets", "banned", "telegram"
            };

            if (controllerNames.Contains(shortCode.ToLower()))
            {
                // Redirect to main domain
                var path = string.IsNullOrEmpty(token) ? $"/{shortCode}" : $"/{shortCode}/{token}";
                var redirectUrl = $"https://portal.gittibilsms.com{path}{Request.QueryString}";
                Console.WriteLine($"[RedirectController] Redirecting controller path to: {redirectUrl}");
                return Redirect(redirectUrl);
            }

            // Continue with normal short URL lookup
            var shortUrl = await _context.ShortUrls
                .FirstOrDefaultAsync(s => s.ShortCode == shortCode && s.IsActive);

            if (shortUrl == null)
            {
                Console.WriteLine($"[RedirectController] ShortUrl not found for code: {shortCode}");
                return NotFound("Short link not found or has been deactivated.");
            }

            if (shortUrl.ExpiryDate.HasValue && shortUrl.ExpiryDate < DateTime.Now)
            {
                Console.WriteLine($"[RedirectController] ShortUrl expired for code: {shortCode}");
                return NotFound("This short link has expired.");
            }

            if (shortUrl.MaxClicks.HasValue && shortUrl.TotalClicks >= shortUrl.MaxClicks)
            {
                Console.WriteLine($"[RedirectController] ShortUrl reached max clicks for code: {shortCode}");
                return NotFound("This short link has reached its maximum number of clicks.");
            }

            // ✅ Decode phone number from Base62 token
            string phoneNumber = null;
            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    phoneNumber = DecodePhoneFromBase62(token);
                    Console.WriteLine($"[ShortUrl-Click] Token: {token} → Decoded Phone: {phoneNumber} → ShortCode: {shortCode}");
                }
                catch
                {
                    Console.WriteLine($"[ShortUrl-Click] Failed to decode token: {token}");
                }
            }
            //else
            //{
            //    Console.WriteLine($"[ShortUrl-Click] No token - phone will be NULL for ShortCode: {shortCode}");
            //}

            // ✅ Device Detection
            var userAgentString = Request.Headers["User-Agent"].ToString();
            var parser = Parser.GetDefault();
            var clientInfo = parser.Parse(userAgentString);

            // ✅ Update click count (always increment)
            shortUrl.TotalClicks++;

            // ✅ Check if device is iOS (iPhone or iPad)
            var isIOS = userAgentString.ToLower().Contains("iphone") || userAgentString.ToLower().Contains("ipad");

            // ✅ Log click with phone number ONLY if iOS
            if (isIOS)
            {
                // Only log if this phone number hasn't been recorded for this short URL
                bool isDuplicate = false;
                if (!string.IsNullOrEmpty(phoneNumber))
                {
                    isDuplicate = await _context.ShortUrlClicks
                        .AnyAsync(c => c.ShortUrlId == shortUrl.Id && c.PhoneNumber == phoneNumber);
                }
                if (!isDuplicate)
                {
                    _context.ShortUrlClicks.Add(new ShortUrlClick
                    {
                        ShortUrlId = shortUrl.Id,
                        ShortCode = shortCode,
                        PhoneNumber = phoneNumber,
                        DeviceType = GetDeviceType(userAgentString),
                        OperatingSystem = clientInfo.OS.Family + " " + clientInfo.OS.Major,
                        Browser = clientInfo.UA.Family + " " + clientInfo.UA.Major,
                        IpAddress = GetClientIpAddress(),
                        UserAgent = userAgentString,
                        ClickedAt = DateTime.Now
                    });

                 // Console.WriteLine($"[ShortUrl-Click] iOS - New click logged for ShortCode: {shortCode}, Phone: {phoneNumber}");
                }
                //else
                //{
                //    Console.WriteLine($"[ShortUrl-Click] iOS - Duplicate phone {phoneNumber} for ShortCode: {shortCode} - Skipped");
                //}
            }

            await _context.SaveChangesAsync();
            //Console.WriteLine($"[RedirectController] Redirecting to: {shortUrl.OriginalUrl}");
            return Redirect(shortUrl.OriginalUrl);
        }

        private static string DecodePhoneFromBase62(string encoded)
        {
            long number = 0;
            foreach (char c in encoded)
            {
                int index = Base62Chars.IndexOf(c);
                if (index < 0) throw new ArgumentException($"Invalid Base62 character: {c}");
                number = number * 62 + index;
            }
            return number.ToString();
        }

        private string GetDeviceType(string userAgent)
        {
            var ua = userAgent.ToLower();
            if (ua.Contains("iphone") || (ua.Contains("android") && !ua.Contains("tablet")))
                return "Mobile";
            if (ua.Contains("ipad") || ua.Contains("tablet"))
                return "Tablet";
            return "Desktop";
        }

        private string GetClientIpAddress()
        {
            var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
                return forwardedFor.Split(',')[0].Trim();

            var cfIp = Request.Headers["CF-Connecting-IP"].FirstOrDefault();
            if (!string.IsNullOrEmpty(cfIp))
                return cfIp;

            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }
    }
}