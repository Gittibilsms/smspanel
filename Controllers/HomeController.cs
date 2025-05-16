using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace GittBilSmsCore.Controllers
{

    public class HomeController(IStringLocalizer<HomeController> localizer) : Controller
    {
        public IActionResult Index()
        {
         
            return View();
        }

        [HttpPost]
        public IActionResult SendSms(string number, string message)
        {
            // TODO: Add SMS sending logic here
            return Ok(); // Let the front-end know it succeeded
        }
    }

}
