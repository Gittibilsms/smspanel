using System.ComponentModel.DataAnnotations;
using GittBilSmsCore.Enums;

namespace GittBilSmsCore.Models
{

    public class Notifications
    {
        [Key]
        public int NotificationId { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }


        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; }

        public int? CompanyId { get; set; }
        public int? OrderId { get; set; }  // ← new
        public int? UserId { get; set; }  // ← new
        public NotificationType Type { get; set; }
    }
}
