using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace GittBilSmsCore.Models 
{
    public enum TwoFactorMethod
    {
        None = 0,
        Authenticator = 1,
        Email = 2
    }

    public class User : IdentityUser<int>
    {
        public int? CompanyId { get; set; } // Null for Admin users
        public string FullName { get; set; }

        [NotMapped]
        public string Password { get; set; }

        public bool? IsMainUser { get; set; }
        public string? UserType { get; set; }

        public int? Quota { get; set; }
        public string? QuotaType { get; set; }
        public string? VerificationType { get; set; }

        public bool IsActive { get; set; }
        public string? ProfilePhotoUrl { get; set; }
        public string? TwoFactorSecretKey { get; set; }

        public bool IsTwoFactorEnabled { get; set; } = false;

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int? CreatedByUserId { get; set; }

        public Company? Company { get; set; }
        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
        public TwoFactorMethod PreferredTwoFactorMethod { get; set; } = TwoFactorMethod.None;
    }
}