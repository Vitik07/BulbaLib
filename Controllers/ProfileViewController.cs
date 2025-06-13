using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BulbaLib.Controllers
{
    public class ProfileViewController : Controller
    {
        [HttpGet("/profile/{id:int}")]
        public IActionResult Profile(int id)
        {
            int? currentUserId = null;
            if (User.Identity.IsAuthenticated)
            {
                currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            }

            if (currentUserId.HasValue && id == currentUserId.Value)
            {
                // Свой профиль
                return View("~/Views/Profile/Profile.cshtml");
            }
            else
            {
                // Чужой профиль
                ViewBag.UserId = id;
                return View("~/Views/Profile/ProfileView.cshtml");
            }
        }
    }
}