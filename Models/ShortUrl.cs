using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace GittBilSmsCore.Models
{
    public class ShortUrl
    {
        [Key]
        public long Id { get; set; }

        [Required]
        [MaxLength(10)]
        public string ShortCode { get; set; }

        [Required]
        [MaxLength(2048)]
        public string OriginalUrl { get; set; }

        [MaxLength(500)]
        public string Title { get; set; }

        [Required]
        public int CompanyId { get; set; }

        public int? CampaignId { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public int CreatedBy { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public bool IsActive { get; set; } = true;

        public int? MaxClicks { get; set; }

        public int TotalClicks { get; set; } = 0;

        // Navigation properties
        [ForeignKey("CompanyId")]
        public virtual Company Company { get; set; }

        [ForeignKey("CreatedBy")]
        public virtual User CreatedByUser { get; set; }
    }
}
