using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GittBilSmsCore.Models
{
    public class Role
    {
        [Key]
        public int RoleId { get; set; }
        public string RoleName { get; set; }
        public bool IsReadOnly { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsGlobal { get; set; } = true;
        public int? CreatedByCompanyId { get; set; }
        [JsonIgnore]
        public ICollection<UserRole> UserRoles { get; set; }
        public ICollection<RolePermission> RolePermissions { get; set; }

    }
}
