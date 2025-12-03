namespace GittBilSmsCore.ViewModels
{
    public class SendSmsViewModel
    {
        public int CompanyId { get; set; }
        public int? DirectoryId { get; set; }
        public int? PastMessageId { get; set; }
        public int SelectedApiId { get; set; }
        public string Message { get; set; }
        public string PhoneNumbers { get; set; }
        public DateTime? ScheduledSendDate { get; set; }
        public int? TotalSmsCount { get; set; }
        public bool HasName { get; set; }
        public string FileMode { get; set; } = "standard";
        public string SelectedCustomColumn { get; set; } = string.Empty;
        public string SelectedNumberColumn { get; set; } = string.Empty;
        public string? SelectedCustomColumnKey { get; set; }
        public string? SelectedNumberColumnKey { get; set; }
        public IFormFile[] files { get; set; }
        public string RecipientsJson { get; set; } = string.Empty;
        public string TempUploadId { get; set; }
    }
}
