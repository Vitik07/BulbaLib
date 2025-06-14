using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Controllers
{
    public class AuthViewController : Controller
    {
        [HttpGet("/login")]
        public IActionResult Login()
        {
            return View("~/Views/Auth/Login.cshtml");
        }

        [HttpGet("/auth/register")]
        public IActionResult Register()
        {
            return View("~/Views/Auth/Register.cshtml");
        }
    }
}