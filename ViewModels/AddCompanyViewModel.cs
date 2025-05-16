namespace GittBilSmsCore.ViewModels
{
    public class AddCompanyViewModel
    {
        // Company Info
        public string CompanyName { get; set; }
        public bool IsTrustedSender { get; set; }
        public bool IsRefundable { get; set; }
        public bool CanSendSupportRequest { get; set; }
        public int? Apid { get; set; }
        public string CurrencyCode { get; set; }
        public decimal? LowPrice { get; set; }
        public decimal? MediumPrice { get; set; }
        public decimal? HighPrice { get; set; }

        // User Info
        public string FullName { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Password { get; set; }
    }
}
