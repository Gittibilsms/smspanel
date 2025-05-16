using System;
using System.ComponentModel.DataAnnotations;

public class Directory
{
    [Key]
    public int DirectoryId { get; set; }
    public int CompanyId { get; set; }
    public int CreatedByUserId { get; set; }
    public string DirectoryName { get; set; }
    public string FileName { get; set; }
    public string FilePath { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime? UploadDate { get; set; }
    public DateTime CreatedAt { get; set; }
}