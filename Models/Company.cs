using System.ComponentModel.DataAnnotations.Schema;

namespace GittBilSmsCore.Models
{
    public class Company
    {
        public int CompanyId { get; set; }
        public string CompanyName { get; set; }
        public bool IsTrustedSender { get; set; }
        public bool IsRefundable { get; set; }
        public bool CanSendSupportRequest { get; set; }
        public int? Apid { get; set; }
        public string CurrencyCode { get; set; }
        public decimal? LowPrice { get; set; }
        public decimal? MediumPrice { get; set; }
        public decimal? HighPrice { get; set; }
        public string Pricing { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; } 
        public DateTime? UpdatedAt { get; set; }
        public decimal? CurrentBalance { get; set; }
        public decimal? Credit { get; set; }
        public decimal? CreditLimit { get; set; }
        public ICollection<User> Users { get; set; }
        [ForeignKey("Apid")]
        public Api Api { get; set; }

    }
}
