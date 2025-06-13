using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Controllers
{
    public class CatalogViewController : Controller
    {
        [HttpGet("/catalog")]
        public IActionResult Index()
        {
            return View("~/Views/Catalog/Catalog.cshtml"); // ищет Views/Catalog/Catalog.cshtml или Views/Shared/Catalog.cshtml
        }
    }
}