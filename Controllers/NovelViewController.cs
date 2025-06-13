using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Controllers
{
    public class NovelViewController : Controller
    {
        [HttpGet("/novel/{id:int}")]
        public IActionResult Details(int id)
        {
            ViewBag.NovelId = id;
            return View("~/Views/Novel/Novel.cshtml");
        }
    }
}