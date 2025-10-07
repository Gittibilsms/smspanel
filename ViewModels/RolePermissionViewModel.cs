
using GittBilSmsCore.Models;

namespace GittBilSmsCore.ViewModels
    {
        public class RolePermissionViewModel
        {
            public int RoleId { get; set; }
            public string RoleName { get; set; }
            public List<PermissionItem> UserPermissions { get; set; } = new();
            public List<PermissionItem> DealerPermissions { get; set; } = new();
            public bool ConnectUserAccount { get; set; }
            public bool RoleAssignmentAuthority { get; set; }
            public bool IsReadOnly { get; set; }
        public List<string> SelectedPermissions { get; set; } = new List<string>();
        public DateTime CreatedAt { get; internal set; }
        public DateTime UpdatedAt { get; internal set; }
    }
    public class UserEditViewModel
    {
        public int Id { get; set; }
        public int? CompanyId { get; set; }
        public string FullName { get; set; }

        public string? createdBy { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public bool IsActive { get; set; }
        public string QuotaType { get; set; }
        public int? Quota { get; set; }

        public List<int> SelectedRoleIds { get; set; } = new();

        // RoleId → List of Permissions
        public Dictionary<int, List<RolePermission>> RolePermissions { get; set; } = new();

        public List<Role> AllRoles { get; set; } = new();
    }
    public class PermissionItem
        {
            public string ModuleName { get; set; }
            public bool Read { get; set; }
            public bool Create { get; set; }
            public bool Edit { get; set; }
            public bool Delete { get; set; }
        }
    }
