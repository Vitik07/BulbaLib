using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;

namespace BulbaLib.Controllers
{
    public class DownloadViewController : Controller
    {
        private readonly MySqlService _db;

        public DownloadViewController(MySqlService db)
        {
            _db = db;
        }

        [HttpGet]
        [Route("download")]
        public IActionResult Index(int? id)
        {
            if (id == null)
                return RedirectToAction("Index", "Home");

            var novel = _db.GetNovel(id.Value);
            if (novel == null)
                return NotFound();

            ViewData["NovelId"] = id.Value;
            ViewData["NovelTitle"] = novel.Title;

            // return View("Download"); // если папка DownloadView
            return View("~/Views/Download/Download.cshtml"); // если папка Download
        }
    }
}