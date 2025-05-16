using System;
using System.ComponentModel.DataAnnotations;

public class Message
{
    [Key]
    public int MessageId { get; set; }
    public int CompanyId { get; set; }
    public int CreatedByUserId { get; set; }
    public string MessageText { get; set; }
    public DateTime CreatedAt { get; set; }
}