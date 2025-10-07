using Microsoft.AspNetCore.Mvc.Rendering;

namespace GittBilSmsCore.ViewModels
{
    public class AddCompanyViewModel
    {
        public string CompanyName { get; set; }
        public bool IsTrustedSender { get; set; }
        public bool IsRefundable { get; set; }
        public bool? CanSendSupportRequest { get; set; }
        public int? Apid { get; set; }
        public List<SelectListItem> ApiSelectList { get; set; }
        // Pricing
        public string CurrencyCode { get; set; }
        public decimal LowPrice { get; set; }
        public decimal MediumPrice { get; set; }
        public decimal HighPrice { get; set; }

        public string Pricing { get; set; }

        // User Information
        public string FullName { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Password { get; set; }
    }
}
