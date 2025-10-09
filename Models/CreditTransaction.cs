namespace GittBilSmsCore.Models
{
    public class CreditTransaction
    {
        public int CreditTransactionId { get; set; }
        public int CompanyId { get; set; }
        public string TransactionType { get; set; } // "Credit added", "Credit deleted", "SMS sent"
        public decimal Credit { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal UnitPrice { get; set; }
        public string Currency { get; set; }
        public string Note { get; set; }
        public DateTime TransactionDate { get; set; }

        public Company Company { get; set; } // Optional navigation
    }
}
