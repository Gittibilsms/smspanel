using System;
using System.ComponentModel.DataAnnotations;

namespace GittBilSmsCore.Models
{
    /// <summary>
    /// Tracks temporary file uploads for large recipient lists.
    /// Files are stored on disk, this table tracks metadata.
    /// </summary>
    public class TempUpload
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Unique identifier for this upload (GUID)
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string TempId { get; set; }

        /// <summary>
        /// Original filename uploaded by user
        /// </summary>
        [MaxLength(255)]
        public string? OriginalFileName { get; set; }

        /// <summary>
        /// Number of recipients parsed from file
        /// </summary>
        public int RecipientCount { get; set; }

        /// <summary>
        /// User who uploaded
        /// </summary>
        public int? UserId { get; set; }

        /// <summary>
        /// Company ID
        /// </summary>
        public int? CompanyId { get; set; }

        /// <summary>
        /// Whether this upload has custom columns (name + number)
        /// </summary>
        public bool HasCustomColumns { get; set; }

        /// <summary>
        /// Name column key if custom
        /// </summary>
        [MaxLength(50)]
        public string? NameColumnKey { get; set; }

        /// <summary>
        /// Number column key if custom
        /// </summary>
        [MaxLength(50)]
        public string? NumberColumnKey { get; set; }

        /// <summary>
        /// Path to the saved recipients file
        /// </summary>
        [MaxLength(500)]
        public string? FilePath { get; set; }

        /// <summary>
        /// When this was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Auto-expire after this time (default: 2 hours)
        /// </summary>
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(2);

        /// <summary>
        /// Whether this upload has been used (linked to an order)
        /// </summary>
        public bool IsUsed { get; set; } = false;

        /// <summary>
        /// OrderId if this upload was used
        /// </summary>
        public int? OrderId { get; set; }
    }
}