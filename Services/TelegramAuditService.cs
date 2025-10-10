using GittBilSmsCore.Data;
using GittBilSmsCore.Models;


namespace GittBilSmsCore.Services
{
    public class TelegramAuditService
    {
        private readonly GittBilSmsDbContext _context;
        public TelegramAuditService(GittBilSmsDbContext db) => _context = db;

        public async Task<long> LogAsync(string entityType, string entityId, string action, int? performedById, object? data = null, CancellationToken ct = default)
        {
            var json = data is null ? null : System.Text.Json.JsonSerializer.Serialize(data);
            var row = new TelegramAuditTrail
            {
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                PerformedById = performedById,
                DataJson = json
            };
            _context.TelegramAuditTrails.Add(row);
            await _context.SaveChangesAsync(ct);
            return row.Id;
        }
    }
}
