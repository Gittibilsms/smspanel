using System.Text.Json.Serialization;

namespace GittBilSmsCore.Models
{
    public class UserRole
    {
        public int UserId { get; set; }
        [JsonIgnore]
        public User User { get; set; }
        public string? Name { get; set; }
        public int RoleId { get; set; }
        public Role Role { get; set; }
    }
}
