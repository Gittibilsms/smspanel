using Microsoft.AspNetCore.Mvc;

namespace GittBilSmsCore.Controllers
{
    public class AccountController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        public IActionResult Login(string email, string password)
        {
            Console.WriteLine("LOGIN POST HIT");

            if (email == "admin@gittbil.com" && password == "123456")
            {
                // TODO: set session or cookie for logged in user
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Error = "Invalid Credentials.";
            return View("Index");
        }
        public IActionResult Logout()
        {
            // Clear session or authentication cookie here if needed
            return RedirectToAction("Index", "Account");
        }
    }
}
