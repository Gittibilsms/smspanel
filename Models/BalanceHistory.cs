namespace GittBilSmsCore.Models
{
    public class BalanceHistory
    {
        public int BalanceHistoryId { get; set; }
        public int CompanyId { get; set; }
        public decimal Amount { get; set; } // -amount for send, +amount for top-up
        public string Action { get; set; } // "Deduct on Send", "Top-up", "Refund", etc.
        public DateTime CreatedAt { get; set; }
        public int? CreatedByUserId { get; set; }

        // Optional → for FK navigation
        public Company Company { get; set; }
    }
}
