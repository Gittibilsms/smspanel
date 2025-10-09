using GittBilSmsCore.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GittBilSmsCore.ViewModels
{
    public class CompanyDetailsViewModel
    {
        public Company Company { get; set; }

        public int CompanyId { get; set; }
        public List<CreditTransaction> CreditTransactions { get; set; }
        public List<decimal> DistinctPricingOptions { get; set; }
        public List<User> CompanyUsers { get; set; }
        public List<SelectListItem> ApiList { get; set; }
        public string LatestUnitPrice { get; set; }
    }
}
