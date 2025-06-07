using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;

namespace BulbaLib.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProfileController : ControllerBase
    {
        private readonly SqliteService _db;

        public ProfileController(SqliteService db)
        {
            _db = db;
        }

        // GET /api/profile?userId=...
        [HttpGet]
        public IActionResult GetProfile([FromQuery] int userId)
        {
            if (userId == 0)
                return BadRequest(new { error = "User ID required" });

            var (user, novelsByStatus) = _db.GetProfile(userId);
            if (user == null)
                return NotFound(new { error = "Пользователь не найден" });

            object ToNovelObj(BulbaLib.Services.Novel n) => new
            {
                id = n.Id,
                title = n.Title,
                image = n.Image != null ? Convert.ToBase64String(n.Image) : null
            };

            return Ok(new
            {
                user = new
                {
                    id = user.Id,
                    login = user.Login,
                    hasAvatar = user.Avatar != null
                },
                reading = novelsByStatus.TryGetValue("reading", out var reading) ? reading.Select(ToNovelObj) : Enumerable.Empty<object>(),
                read = novelsByStatus.TryGetValue("read", out var read) ? read.Select(ToNovelObj) : Enumerable.Empty<object>(),
                favorites = novelsByStatus.TryGetValue("favorites", out var fav) ? fav.Select(ToNovelObj) : Enumerable.Empty<object>(),
                abandoned = novelsByStatus.TryGetValue("abandoned", out var abn) ? abn.Select(ToNovelObj) : Enumerable.Empty<object>()
            });
        }
    }
}