using System;
using System.ComponentModel.DataAnnotations;

public class Api
{
    [Key]
    public int ApiId { get; set; }
    public int CompanyId { get; set; }
    public string ServiceType { get; set; }
    public string ServiceName { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string Originator { get; set; }
    public int CreatedByUserId { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}