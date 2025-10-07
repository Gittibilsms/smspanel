using System;
using System.ComponentModel.DataAnnotations;
using GittBilSmsCore.Models;

namespace GittBilSmsCore.Models
{
    public class Order
    {
        [Key]
        public int OrderId { get; set; }

        public int CompanyId { get; set; }
        public int? DirectoryId { get; set; }
        public int? PastMessageId { get; set; }
        public int ApiId { get; set; }

        public string? SubmissionType { get; set; }
        public DateTime? ScheduledSendDate { get; set; }
        public string? MessageText { get; set; }
        public string? FilePath { get; set; }
        public int LoadedCount { get; set; }
        public int ProcessedCount { get; set; }
        public int UnsuccessfulCount { get; set; }

        public bool Refundable { get; set; }
        public bool Returned { get; set; }
        public DateTime? ReturnDate { get; set; }

        public string CurrentStatus { get; set; }
        public int? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation Properties
        public Company Company { get; set; }
        public Api Api { get; set; }
        public User CreatedByUser { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? SmsOrderId { get; set; }
        public DateTime? StartedAt { get; set; }
        public bool ReportLock { get; set; }
        public DateTime? ReportedAt { get; set; }

        public int? TotalCount { get; set; }
        public int? DeliveredCount { get; set; }
        public int? UndeliveredCount { get; set; }
        public int? ExpiredCount { get; set; }
        public int? InvalidCount { get; set; }
        public int? BlacklistedCount { get; set; }
        public int? RepeatedCount { get; set; }
        public int? BannedCount { get; set; }
        public decimal? RefundAmount { get; set; }
        public string? ApiErrorResponse { get; set; }
        public int? WaitingCount { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? PlaceholderColumn { get; set; }

        public int? SmsCount { get; set; }
        public decimal? PricePerSms { get; set; }
        public decimal? TotalPrice { get; set; }
        public virtual ICollection<OrderAction> Actions { get; set; } = new List<OrderAction>();
        public ICollection<OrderRecipient> Recipients { get; set; }
        public ICollection<ApiCallLog> ApiCalls { get; set; }
    }
}