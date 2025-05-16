namespace GittBilSmsCore.Models
{
    public class CompanyUser
    {
        public int UserId { get; set; }
        public int CompanyId { get; set; }
        public string FullName { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Password { get; set; }

        public bool IsMainUser { get; set; } = true;
        public string UserType { get; set; } = "Admin";
        public int? Quota { get; set; }
        public string QuotaType { get; set; }
        public string VerificationType { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int? CreatedByUserId { get; set; }

        public Company Company { get; set; }
    }
}
