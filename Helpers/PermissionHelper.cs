using GittBilSmsCore.Models;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
namespace GittBilSmsCore.Helpers
{
    public static class PermissionHelper
    {
        public static List<RolePermission> GetPermissions(this ISession session)
        {
            var json = session.GetString("UserPermissions");

            if (string.IsNullOrEmpty(json))
            {
                Console.WriteLine("No permissions found in session.");
                return new List<RolePermission>();
            }

            var permissions = JsonSerializer.Deserialize<List<RolePermission>>(json);

            // 🔍 Log each permission
            foreach (var perm in permissions)
            {
                Console.WriteLine($"[SessionPerm] Module: {perm.Module}, Read: {perm.CanRead}, Create: {perm.CanCreate}, Edit: {perm.CanEdit}, Delete: {perm.CanDelete}");
            }

            return permissions;
        }

        public static bool HasPermission(this ISession session, string module, string action, bool isMainUser = false)

        {
            Console.WriteLine($"Checking permission: {module} - {action}");

            var permissions = session.GetPermissions();

            return permissions.Any(p =>
               string.Equals(p.Module, module, StringComparison.OrdinalIgnoreCase) &&
               action switch
               {
                   "Read" => p.CanRead,
                   "Create" => p.CanCreate,
                   "Edit" => p.CanEdit,
                   "Delete" => p.CanDelete,
                   _ => false
               }
               &&
               // ✅ Only restrict by isMainUser if RoleId is 5 and RequiresMainUser is true
               (!p.RequiresMainUser.GetValueOrDefault() || isMainUser || p.RoleId != 5)
           );

    
        }

        // ✅ New centralized check
        public static bool HasAll(this ISession session, params (string Module, string Action)[] checks)
        {
            var permissions = session.GetPermissions();

            foreach (var check in checks)
            {
                var match = permissions.Any(p =>
                    string.Equals(p.Module, check.Module, StringComparison.OrdinalIgnoreCase) &&
                    check.Action switch
                    {
                        "Read" => p.CanRead,
                        "Create" => p.CanCreate,
                        "Edit" => p.CanEdit,
                        "Delete" => p.CanDelete,
                        _ => false
                    });

                if (!match)
                    return false; // If one check fails, return false
            }

            return true; // All passed
        }

        public static bool HasSpecial(this ISession session, string module)
        {
            var permissions = session.GetPermissions();
            return permissions.Any(p =>
                string.Equals(p.Module, module, StringComparison.OrdinalIgnoreCase) &&
                p.Special);
        }
        public static bool HasAny(this ISession session, string module, params string[] actions)
        {
            var permissions = session.GetPermissions();

            foreach (var action in actions)
            {
                var match = permissions.Any(p =>
                    string.Equals(p.Module, module, StringComparison.OrdinalIgnoreCase) &&
                    action switch
                    {
                        "Read" => p.CanRead,
                        "Create" => p.CanCreate,
                        "Edit" => p.CanEdit,
                        "Delete" => p.CanDelete,
                        _ => false
                    });

                if (match) return true;
            }

            return false;
        }
        public static bool IsCompanyUser(this ISession session)
        {
            var userType = session.GetString("UserType") ?? "";
            return userType.Equals("CompanyUser", StringComparison.OrdinalIgnoreCase);
        }
    }
}
