using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Controllers
{
    public class ChapterViewController : Controller
    {
        [HttpGet("/chapter/{id:int}")]
        public IActionResult Read(int id)
        {
            ViewBag.ChapterId = id;
            return View("~/Views/Chapter/Chapter.cshtml");
        }
    }
}