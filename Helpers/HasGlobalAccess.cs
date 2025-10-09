using System.Security.Claims;

namespace GittBilSmsCore.Helpers
{
    public static class RoleHelper
    {
        public static bool HasGlobalAccess(ClaimsPrincipal user)
        {
            return user.IsInRole("Admin") ||
                   user.IsInRole("Support") ||
                   user.IsInRole("Supervisor") ||
                   user.IsInRole("PanelUser");
        }
    }
}
