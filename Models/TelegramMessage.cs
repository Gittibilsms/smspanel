using GittBilSmsCore.Models;

namespace GittBilSmsCore.Models
{
    public class TelegramMessage
    {
        public long Id { get; set; }
        public MessageDirection Direction { get; set; }
        public int? UserId { get; set; }
        public User? User { get; set; }  
        public long? TelegramMessageId { get; set; }
        public long ChatId { get; set; }
        public Guid CorrelationId { get; set; } = Guid.NewGuid();
        public string Body { get; set; } = default!;
        public string Status { get; set; } = "Sent";
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
    public enum MessageDirection : byte { Outbound = 0, Inbound = 1 }
}
