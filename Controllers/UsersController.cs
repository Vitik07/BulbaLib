using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;
using System.Security.Claims;

namespace BulbaLib.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly MySqlService _db;
        private readonly IWebHostEnvironment _env;

        public UsersController(MySqlService db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // GET /api/users/status?novelId=1
        [HttpGet("status")]
        public IActionResult GetUserNovelStatus([FromQuery] int novelId)
        {
            if (novelId == 0)
                return Ok(new { status = (string)null });

            int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            var status = _db.GetUserNovelStatus(userId, novelId);
            return Ok(new { status });
        }

        // POST /api/users/status
        [HttpPost("status")]
        public IActionResult UpdateStatus([FromBody] StatusRequest req)
        {
            if (req.NovelId == 0 || string.IsNullOrWhiteSpace(req.Status))
                return BadRequest(new { error = "novelId и status обязательны" });

            int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            _db.UpdateUserNovelStatus(userId, req.NovelId, req.Status);
            return Ok(new { message = "Статус обновлён", status = req.Status });
        }

        // GET /api/users/bookmarks
        [HttpGet("bookmarks")]
        public IActionResult GetBookmarks()
        {
            int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            var bookmarks = _db.GetBookmarks(userId);
            return Ok(bookmarks);
        }

        // POST /api/users/bookmarks
        [HttpPost("bookmarks")]
        public IActionResult AddOrUpdateBookmark([FromBody] BookmarkRequest req)
        {
            if (req.NovelId == 0 || req.ChapterId == 0)
                return BadRequest(new { error = "NovelId and ChapterId required" });

            int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            _db.AddOrUpdateBookmark(userId, req.NovelId, req.ChapterId);
            return Ok(new { message = "Bookmark updated" });
        }

        // DELETE /api/users/bookmarks
        [HttpDelete("bookmarks")]
        public IActionResult RemoveBookmark([FromBody] BookmarkRequest req)
        {
            if (req.NovelId == 0 || req.ChapterId == 0)
                return BadRequest(new { error = "NovelId and ChapterId required" });

            int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            _db.RemoveBookmark(userId, req.NovelId, req.ChapterId);
            return Ok(new { message = "Bookmark deleted" });
        }

        // GET /api/users/avatar
        [HttpGet("avatar")]
        public IActionResult GetUserAvatar()
        {
            int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            var user = _db.GetUser(userId);
            if (user == null || user.Avatar == null)
            {
                var defaultPath = System.IO.Path.Combine(_env.WebRootPath, "Resource", "default-avatar.png");
                return PhysicalFile(defaultPath, "image/png");
            }
            return File(user.Avatar, "image/png");
        }

        // POST /api/users/avatar
        [HttpPost("avatar")]
        public IActionResult UploadUserAvatar()
        {
            int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);

            var file = Request.Form.Files["avatar"];
            if (file == null)
                return BadRequest(new { error = "No avatar uploaded" });

            using var ms = new MemoryStream();
            file.CopyTo(ms);
            var avatar = ms.ToArray();
            _db.UpdateUserAvatar(userId, avatar);
            return Ok(new { message = "Аватар обновлён" });
        }

        // GET /api/users/me
        [HttpGet("me")]
        public IActionResult GetUser()
        {
            int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            var user = _db.GetUser(userId);
            if (user == null)
                return NotFound(new { error = "User not found" });

            return Ok(new
            {
                id = user.Id,
                login = user.Login,
                avatar = user.Avatar != null ? Convert.ToBase64String(user.Avatar) : null
            });
        }

        // GET /api/users/{userId} -- публичный просмотр чужого профиля (без авторизации)
        [AllowAnonymous]
        [HttpGet("{userId:int}")]
        public IActionResult GetPublicUser(int userId)
        {
            var user = _db.GetUser(userId);
            if (user == null)
                return NotFound(new { error = "User not found" });

            return Ok(new
            {
                id = user.Id,
                login = user.Login,
                avatar = user.Avatar != null ? Convert.ToBase64String(user.Avatar) : null
            });
        }
    }

    // DTOs
    public class StatusRequest
    {
        public int NovelId { get; set; }
        public string Status { get; set; }
    }

    public class BookmarkRequest
    {
        public int NovelId { get; set; }
        public int ChapterId { get; set; }
    }
}