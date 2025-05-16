using System;
using System.ComponentModel.DataAnnotations;

public class Order
{
    [Key]
    public int OrderId { get; set; }
    public int CompanyId { get; set; }
    public int? DirectoryId { get; set; }
    public int? PastMessageId { get; set; }
    public int ApiId { get; set; }
    public string SubmissionType { get; set; }
    public DateTime? ScheduledSendDate { get; set; }
    public string MessageText { get; set; }
    public int LoadedCount { get; set; }
    public int ProcessedCount { get; set; }
    public int UnsuccessfulCount { get; set; }
    public bool Refundable { get; set; }
    public bool Returned { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string CurrentStatus { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}