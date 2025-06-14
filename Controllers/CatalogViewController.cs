using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services; // Added
using BulbaLib.Models;   // Added
using System.Security.Claims; // Added

namespace BulbaLib.Controllers
{
    public class CatalogViewController : Controller
    {
        private readonly MySqlService _mySqlService; // Added
        private readonly PermissionService _permissionService; // Added

        public CatalogViewController(MySqlService mySqlService, PermissionService permissionService) // Added
        {
            _mySqlService = mySqlService;
            _permissionService = permissionService;
        }

        private User GetCurrentUser() // Added
        {
            if (!User.Identity.IsAuthenticated)
            {
                return null;
            }
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
            {
                return null;
            }
            return _mySqlService.GetUser(userId);
        }

        [HttpGet("/catalog")]
        public IActionResult Index()
        {
            var currentUser = GetCurrentUser();
            bool canAddNewNovel = false;
            if (currentUser != null)
            {
                canAddNewNovel = _permissionService.CanAddNovelDirectly(currentUser) || _permissionService.CanSubmitNovelForModeration(currentUser);
            }
            ViewData["CanAddNewNovel"] = canAddNewNovel;

            return View("~/Views/Catalog/Catalog.cshtml");
        }

        // Placeholder for Novel Detail View if it's also served by this controller
        // GET /novel/{id}
        // [HttpGet("/novel/{id}")]
        // public IActionResult Novel(int id)
        // {
        //     var novel = _mySqlService.GetNovel(id);
        //     if (novel == null) return NotFound();
        //     // ... fetch chapters, user, permissions for edit/delete for this specific novel
        //     return View("~/Views/Novel/Novel.cshtml", novel);
        // }
    }
}