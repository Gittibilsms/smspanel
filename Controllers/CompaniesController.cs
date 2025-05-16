using Microsoft.AspNetCore.Mvc;

namespace GittBilSmsCore.Controllers
{
    public class CompaniesController : Controller
    {
        public IActionResult Index() => View();
    }
}
