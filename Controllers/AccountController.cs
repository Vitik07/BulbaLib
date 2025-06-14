using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Controllers
{
    public class AccountController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
