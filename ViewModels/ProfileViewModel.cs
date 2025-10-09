using System.ComponentModel.DataAnnotations;
using GittBilSmsCore.Models;

namespace GittBilSmsCore.ViewModels
{
    public class ProfileViewModel
    {
        [Required]
        [Display(Name = "Full Name")]
        public string FullName { get; set; }

        [Display(Name = "Username")]
        public string Username { get; set; } 

        [Required]
        [EmailAddress]
        [Display(Name = "Email Address")]
        public string Email { get; set; }

        [Phone]
        [Display(Name = "Phone Number")]
        public string PhoneNumber { get; set; }
        public string ExistingProfilePhotoUrl { get; set; } 
        [Display(Name = "Profile Photo URL")]
        public IFormFile ProfilePhoto { get; set; }
        public string ProfilePhotoUrl { get; set; } 

        [Display(Name = "New Password")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; }

        [Display(Name = "Two-Factor Authentication")]
        public bool IsTwoFactorEnabled { get; set; }

        [Display(Name = "Preferred Two-Factor Method")]
        public TwoFactorMethod PreferredTwoFactorMethod { get; set; }

        [Display(Name = "Roles")]
        public List<string> Roles { get; set; } = new List<string>();
        public long? TelegramUserId { get; set; }

        public string? BindTelegramId { get; set; }

        public bool showTelegram { get; set; }
    }
}
