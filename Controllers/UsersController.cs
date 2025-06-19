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
        private readonly ICurrentUserService _currentUserService;

        public UsersController(MySqlService db, IWebHostEnvironment env, ICurrentUserService currentUserService)
        {
            _db = db;
            _env = env;
            _currentUserService = currentUserService;
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
            // int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            // var user = _db.GetUser(userId);
            var user = _currentUserService.GetCurrentUser(); // Use ICurrentUserService

            if (user == null)
            {
                // This case should ideally be handled by [Authorize] if token is invalid.
                // If token is valid but user somehow not found by ICurrentUserService, then 404.
                return NotFound(new { error = "User not found or not authenticated." });
            }


            return Ok(new
            {
                id = user.Id,
                login = user.Login,
                role = user.Role.ToString(), // Added Role
                // avatar = user.Avatar != null ? Convert.ToBase64String(user.Avatar) : null // Old: base64
                avatarUrl = Url.Action("GetUserAvatar", "Users", null, Request.Scheme) // Generates URL like /api/users/avatar
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

        [HttpGet("{userId:int}/avatar")]
        [AllowAnonymous] // Avatars can be public
        public IActionResult GetUserAvatarById(int userId)
        {
            var user = _db.GetUser(userId); // _db is MySqlService
            if (user == null || user.Avatar == null || user.Avatar.Length == 0)
            {
                // _env is IWebHostEnvironment, ensure it's injected if not already
                var defaultAvatarPath = System.IO.Path.Combine(_env.WebRootPath, "Resource", "default-avatar.jpg");
                // Check if default avatar exists to prevent 500 error if it's missing
                if (!System.IO.File.Exists(defaultAvatarPath))
                {
                    // Fallback to a simple 404 if default avatar is also missing
                    return NotFound("Default avatar not found.");
                }
                return PhysicalFile(defaultAvatarPath, "image/jpeg"); // Assuming default is jpg
            }
            return File(user.Avatar, "image/png"); // Assuming user avatars are stored as png, adjust if necessary
        }

        [HttpGet("search")] // Route will be /api/Users/search
        [Authorize] // Keep Authorize as this is usually for logged-in users selecting authors/translators
        public IActionResult SearchUsersByName([FromQuery] string nameQuery, [FromQuery] int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(nameQuery) || nameQuery.Length < 2)
            {
                return Ok(new List<object>());
            }
            if (limit <= 0) limit = 10;
            if (limit > 50) limit = 50; // Max limit to prevent abuse


            // _db is the instance of MySqlService injected into UsersController
            // Assuming SearchUsersByLogin in MySqlService needs to be updated to accept a limit
            // For now, I'll call the existing method and then take 'limit' items.
            // Ideally, MySqlService.SearchUsersByLogin itself would handle the LIMIT in SQL.
            var users = _db.SearchUsersByLogin(nameQuery); // This method currently fetches up to 10 by default in SQL

            if (users == null)
            {
                return Ok(new List<object>());
            }

            var result = users.Take(limit).Select(u => new { // Apply limit here if not in DB service
                id = u.Id,
                login = u.Login,
                avatarUrl = Url.Action("GetUserAvatarById", "Users", new { userId = u.Id }, Request.Scheme) // Use Url.Action for robust URL generation
            });

            return Ok(result);
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