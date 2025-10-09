using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using GittBilSmsCore.Helpers;
namespace GittBilSmsCore.Models
{
    public class BlacklistNumber
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string Number { get; set; }

        [Required]
        public int CompanyId { get; set; }

        [Required]
        public int CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("CompanyId")]
        public Company Company { get; set; }

        [ForeignKey("CreatedByUserId")]
        public User CreatedByUser { get; set; }
    }
}
