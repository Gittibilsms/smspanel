using Microsoft.AspNetCore.Mvc;

namespace GittBilSmsCore.Controllers
{
    [Route("health")]
    public class HealthController : Controller
    {
        [HttpGet]
        public IActionResult Get() => Ok("Healthy");
    }
}
