using Microsoft.AspNetCore.Mvc;
using BulbaLib.Models;
using BulbaLib.Services;

namespace bulbalib.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly SqliteService _db;
        private readonly IWebHostEnvironment _env;

        public UsersController(SqliteService db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // GET /api/users/{userId}/status?novelId=1
        [HttpGet("{userId}/status")]
        public IActionResult GetUserNovelStatus(int userId, [FromQuery] int novelId)
        {
            if (novelId == 0)
                return Ok(new { status = (string)null });

            var status = _db.GetUserNovelStatus(userId, novelId);
            return Ok(new { status });
        }

        // POST /api/users/{userId}/status
        [HttpPost("{userId}/status")]
        public IActionResult UpdateStatus(int userId, [FromBody] StatusRequest req)
        {
            if (req.NovelId == 0 || string.IsNullOrWhiteSpace(req.Status))
                return BadRequest(new { error = "novelId и status обязательны" });

            _db.UpdateUserNovelStatus(userId, req.NovelId, req.Status);
            return Ok(new { message = "Статус обновлён", status = req.Status });
        }

        // GET /api/users/{userId}/bookmarks
        [HttpGet("{userId}/bookmarks")]
        public IActionResult GetBookmarks(int userId)
        {
            var bookmarks = _db.GetBookmarks(userId);
            return Ok(bookmarks);
        }

        // POST /api/users/{userId}/bookmarks
        [HttpPost("{userId}/bookmarks")]
        public IActionResult AddOrUpdateBookmark(int userId, [FromBody] BookmarkRequest req)
        {
            if (req.NovelId == 0 || req.ChapterId == 0)
                return BadRequest(new { error = "NovelId and ChapterId required" });

            _db.AddOrUpdateBookmark(userId, req.NovelId, req.ChapterId);
            return Ok(new { message = "Bookmark updated" });
        }

        // DELETE /api/users/{userId}/bookmarks
        [HttpDelete("{userId}/bookmarks")]
        public IActionResult RemoveBookmark(int userId, [FromBody] BookmarkRequest req)
        {
            if (req.NovelId == 0 || req.ChapterId == 0)
                return BadRequest(new { error = "NovelId and ChapterId required" });

            _db.RemoveBookmark(userId, req.NovelId, req.ChapterId);
            return Ok(new { message = "Bookmark deleted" });
        }

        // GET /api/users/{userId}/avatar
        [HttpGet("{userId}/avatar")]
        public IActionResult GetUserAvatar(int userId)
        {
            var user = _db.GetUser(userId);
            if (user == null || user.Avatar == null)
            {
                var defaultPath = System.IO.Path.Combine(_env.WebRootPath, "Resource", "default-avatar.png");
                return PhysicalFile(defaultPath, "image/png");
            }
            return File(user.Avatar, "image/png");
        }

        // POST /api/users/{userId}/avatar
        [HttpPost("{userId}/avatar")]
        public IActionResult UploadUserAvatar(int userId)
        {
            var file = Request.Form.Files["avatar"];
            if (file == null)
                return BadRequest(new { error = "No avatar uploaded" });

            using var ms = new MemoryStream();
            file.CopyTo(ms);
            var avatar = ms.ToArray();
            _db.UpdateUserAvatar(userId, avatar);
            return Ok(new { message = "Аватар обновлён" });
        }

        // GET /api/users/{userId}
        [HttpGet("{userId}")]
        public IActionResult GetUser(int userId)
        {
            var user = _db.GetUser(userId);
            if (user == null)
                return NotFound(new { error = "User not found" });

            return Ok(new
            {
                id = user.Id,
                login = user.Login,
                hasAvatar = user.Avatar != null
            });
        }
    }
}