using GittBilSmsCore.Data;
using Microsoft.AspNetCore.Mvc;

namespace GittBilSmsCore.Controllers
{
    public class DealerDashboardController : BaseController
    {
        private readonly GittBilSmsDbContext _context;

        public DealerDashboardController(GittBilSmsDbContext context) : base(context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            if (!HasPermission("Dealer_Read"))
            {
                return Forbid();
            }

            // example data → you can expand
            return View();
        }
    }
}
