namespace GittBilSmsCore.ViewModels
{
    public class OrderDetailsViewModel
    {
        public int OrderId { get; set; }

        // Counts
        public int? LoadedCount { get; set; }
        public int? ProcessedCount { get; set; }
        public int? InvalidCount { get; set; }
        public int? RepeatedCount { get; set; }
        public int? BannedCount { get; set; }
        public int? BlacklistedCount { get; set; }
        public int? UnsuccessfulCount { get; set; }

        // Report counts
        public int? TotalCount { get; set; }
        public int? DeliveredCount { get; set; }
        public int? UndeliveredCount { get; set; }
        public int? WaitingCount { get; set; }
        public int? ExpiredCount { get; set; }

        // Status and dates
        public string CurrentStatus { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? ReportedAt { get; set; }

        // Message
        public string MessageText { get; set; }

        // Related info
        public string CompanyName { get; set; }
        public string ApiName { get; set; }
        public string SubmissionType { get; set; }

        public string CreatedByUserFullName { get; set; }

        // Important → you need this for your ReportLock usage!
        public bool ReportLock { get; set; }
    }
}
