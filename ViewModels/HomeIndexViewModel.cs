using GittBilSmsCore.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GittBilSmsCore.ViewModels
{
    public class HomeIndexViewModel
    {
        public List<Company> Companies { get; set; }
        public SelectList ApiList { get; set; }

        public decimal? LowPrice { get; set; }
        public decimal? MediumPrice { get; set; }
        public decimal? HighPrice { get; set; }
        public decimal? LatestUnitPrice => LowPrice;
        public Company Company { get; set; }
        public List<SelectListItem> ApiLists { get; set; } = new List<SelectListItem>();
        public int? DefaultApiId { get; set; }
        public int? SelectedApiId { get; set; }
    }
}
