using GittBilSmsCore.Models;

namespace GittBilSmsCore.ViewModels
{
    public class UserDetailsViewModel
    {
        public User User { get; set; }
        public List<Role> Roles { get; set; }
        public Dictionary<int, List<RolePermission>> RolePermissions { get; set; }
    }
}
