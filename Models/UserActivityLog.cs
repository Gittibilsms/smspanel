using System.ComponentModel.DataAnnotations;

namespace GittBilSmsCore.Models
{
    public class UserActivityLog
    {
        [Key]
        public int Id { get; set; }         
        public int PerformedByUserId { get; set; }

        [MaxLength(256)]
        public string? PerformedByUserName { get; set; }         
        public int? AffectedUserId { get; set; }

        [MaxLength(256)]
        public string? AffectedUserName { get; set; }
         
        [Required]
        [MaxLength(50)]
        public string ActivityType { get; set; } = string.Empty;         
        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(50)]
        public string? IpAddress { get; set; }

        [MaxLength(500)]
        public string? UserAgent { get; set; }

        [MaxLength(200)]
        public string? Location { get; set; }

        public DateTime CreatedAt { get; set; }

        public int? CompanyId { get; set; }
    }
}
