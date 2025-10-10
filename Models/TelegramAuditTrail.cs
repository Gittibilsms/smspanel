namespace GittBilSmsCore.Models;

public class TelegramAuditTrail
{
    public long Id { get; set; }

    public string EntityType { get; set; } = default!;
    public string EntityId { get; set; } = default!;
    public string Action { get; set; } = default!;

    public int? PerformedById { get; set; }
    public User? PerformedBy { get; set; }

    public string? DataJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
