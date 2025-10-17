using GittBilSmsCore.Models;
using Newtonsoft.Json;
using System.Net.Http;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;
namespace GittBilSmsCore.Helpers
{
    public class SessionHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        public static void SetUserSession(ISession session, User user)
        {
            session.SetInt32("UserId", user.Id);
            session.SetString("UserType", user.UserType ?? "Unknown");
            session.SetInt32("IsMainUser", (user.IsMainUser ?? false) ? 1 : 0);

            if (user.CompanyId.HasValue)
            {
                session.SetInt32("CompanyId", user.CompanyId.Value);
                session.SetInt32("CanSendSupportRequest", user.Company?.CanSendSupportRequest == true ? 1 : 0);

                var isMainUser = (user.IsMainUser ?? false);
                var availableCredit = isMainUser
                    ? (user.Company?.CreditLimit ?? 0)
                    : (user.Quota ?? 0);

                session.SetString("AvailableCredit",
                    availableCredit.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }

            var roleIds = user.UserRoles.Select(ur => ur.RoleId).ToList();
            if (roleIds.Any())
            {
                session.SetString("RoleIds", string.Join(",", roleIds));
                session.SetInt32("RoleId", roleIds.First());

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

                var permissionJson = JsonSerializer.Serialize(permissions);
                session.SetString("UserPermissions", permissionJson);
            }
        }

        public static async Task<string> GetLocationFromIP(string ip)
        {
            var response = await _httpClient.GetStringAsync($"http://ip-api.com/json/{ip}");
            dynamic data = JsonConvert.DeserializeObject(response);

            return $"{data.city}, {data.country}";
        }
        public static string ParseDevice(string userAgent)
        {
            if (userAgent.Contains("Windows")) return "Windows";
            if (userAgent.Contains("Android")) return "Android";
            if (userAgent.Contains("iPhone") || userAgent.Contains("iPad")) return "iOS";
            if (userAgent.Contains("Macintosh")) return "Mac";
            return "Unknown";
        }
    }
        
}
