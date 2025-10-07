using GittBilSmsCore.Models;
using System.Text.Json;
namespace GittBilSmsCore.Helpers
{
    public class SessionHelper
    {
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
    }
        
}
