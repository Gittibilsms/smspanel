using System;
using System.ComponentModel.DataAnnotations;
using GittBilSmsCore.Helpers;
namespace GittBilSmsCore.Models
{
    public class Api
    {
        [Key]
        public int ApiId { get; set; }
        public string ServiceType { get; set; }

        public bool IsDefault { get; set; }
        public string ServiceName { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Originator { get; set; }
        public string? ApiUrl { get; set; }
        public string? ContentType { get; set; }
        public int CreatedByUserId { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
        public DateTime? UpdatedAt { get; set; }

        public bool IsClientApi { get; set; }
    }
}