using System.ComponentModel.DataAnnotations;

namespace GittBilSmsCore.ViewModels
{
    public class ShortUrlViewModel
    {
        public long Id { get; set; }

        public string ShortCode { get; set; }

        public string ShortUrl { get; set; }

        public string OriginalUrl { get; set; }

        public string Title { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public int TotalClicks { get; set; }

        public int? MaxClicks { get; set; }

        public bool IsActive { get; set; }

        public string CompanyName { get; set; }

        public string CreatedByName { get; set; }
         
        public string FormattedShortUrl => $"go2s.me/{ShortCode}";

        public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value < DateTime.Now;

        public bool HasReachedMaxClicks => MaxClicks.HasValue && TotalClicks >= MaxClicks.Value;

        public string StatusText
        {
            get
            {
                if (!IsActive) return "Inactive";
                if (IsExpired) return "Expired";
                if (HasReachedMaxClicks) return "Max Clicks Reached";
                return "Active";
            }
        }
        public string StatusClass
        {
            get
            {
                if (!IsActive || IsExpired || HasReachedMaxClicks) return "danger";
                return "success";
            }
        }
    }
    public class CreateShortUrlViewModel
    {
        [Required(ErrorMessage = "Destination URL is required")]
        [Url(ErrorMessage = "Please enter a valid URL")]
        [Display(Name = "Destination URL")]
        public string DestinationUrl { get; set; }

        [Display(Name = "Title")]
        [MaxLength(500, ErrorMessage = "Title cannot exceed 500 characters")]
        public string Title { get; set; }

        [Display(Name = "Custom back-half")]
        [MaxLength(50, ErrorMessage = "Custom back-half cannot exceed 50 characters")]
        [RegularExpression(@"^[a-zA-Z0-9-_]+$", ErrorMessage = "Only letters, numbers, hyphens and underscores are allowed")]
        public string CustomBackHalf { get; set; }

        [Display(Name = "Expiry Date")]
        [DataType(DataType.DateTime)]
        public DateTime? ExpiryDate { get; set; }

        [Display(Name = "Maximum Clicks")]
        [Range(1, int.MaxValue, ErrorMessage = "Maximum clicks must be greater than 0")]
        public int? MaxClicks { get; set; }
    }
    public class EditShortUrlViewModel
    {
        [Required(ErrorMessage = "Destination URL is required")]
        [Url(ErrorMessage = "Please enter a valid URL")]
        [MaxLength(2048)]
        public string DestinationUrl { get; set; }

        [MaxLength(500)]
        public string? Title { get; set; }

        public DateTime? ExpiryDate { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Maximum clicks must be at least 1")]
        public int? MaxClicks { get; set; }
    }
    public class ShortUrlSettings
    {
        public string BaseUrl { get; set; } = string.Empty;
    }
}
