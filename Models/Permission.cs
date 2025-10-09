namespace GittBilSmsCore.Models
{
    public class Permission
    {
        public int PermissionId { get; set; }
        public string Resource { get; set; } // e.g., "Company", "Order", etc.
        public string Action { get; set; }   // e.g., "Read", "Create", "Edit", "Delete"

        public ICollection<RolePermission> RolePermissions { get; set; }
    }
}
