using GittBilSmsCore.Helpers;
namespace GittBilSmsCore.Models
{
    public class BannedNumber
    {
        public int BannedNumberId { get; set; }
        public string Number { get; set; }
        public string Reason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    }
}
