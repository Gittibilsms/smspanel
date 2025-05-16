using System;
using System.ComponentModel.DataAnnotations;

public class DirectoryNumber
{
    [Key]
    public int DirectoryNumberId { get; set; }
    public int DirectoryId { get; set; }
    public string PhoneNumber { get; set; }
    public string SourceMethod { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
}