using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;
using System.Security.Claims;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;

namespace BulbaLib.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/profile")]
    public class ProfileController : ControllerBase
    {
        private readonly MySqlService _db;

        public ProfileController(MySqlService db)
        {
            _db = db;
        }

        // GET /api/profile — свой профиль (авторизация обязательна)
        [HttpGet]
        public IActionResult GetOwnProfile()
        {
            int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            (User user, Dictionary<string, List<Novel>> novelsByStatus) = _db.GetProfile(userId);
            if (user == null)
                return NotFound(new { error = "Пользователь не найден" });

            object ToNovelObj(BulbaLib.Services.Novel n)
            {
                var covers = string.IsNullOrWhiteSpace(n.Covers) ? null : System.Text.Json.JsonSerializer.Deserialize<List<string>>(n.Covers);
                var cover = (covers != null && covers.Count > 0) ? covers[^1] : null;
                return new
                {
                    id = n.Id,
                    title = n.Title,
                    cover = cover
                };
            }

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

        // GET /api/profile/{id} — публичный профиль по id (для чужих)
        [AllowAnonymous]
        [HttpGet("{id:int}")]
        public IActionResult GetPublicProfile(int id)
        {
            (User user, Dictionary<string, List<Novel>> novelsByStatus) = _db.GetProfile(id);
            if (user == null)
                return NotFound(new { error = "Пользователь не найден" });

            object ToNovelObj(BulbaLib.Services.Novel n)
            {
                var covers = string.IsNullOrWhiteSpace(n.Covers) ? null : JsonSerializer.Deserialize<List<string>>(n.Covers);
                var cover = (covers != null && covers.Count > 0) ? covers[^1] : null;
                return new
                {
                    id = n.Id,
                    title = n.Title,
                    cover = cover
                };
            }

            return Ok(new
            {
                user = new
                {
                    id = user.Id,
                    login = user.Login,
                    avatar = user.Avatar != null ? Convert.ToBase64String(user.Avatar) : null
                },
                novelsByStatus = new
                {
                    favorites = novelsByStatus.TryGetValue("favorites", out var fav) ? fav.Select(ToNovelObj) : Enumerable.Empty<object>(),
                    reading = novelsByStatus.TryGetValue("reading", out var reading) ? reading.Select(ToNovelObj) : Enumerable.Empty<object>(),
                    read = novelsByStatus.TryGetValue("read", out var read) ? read.Select(ToNovelObj) : Enumerable.Empty<object>(),
                    abandoned = novelsByStatus.TryGetValue("abandoned", out var abn) ? abn.Select(ToNovelObj) : Enumerable.Empty<object>()
                }
            });
        }
    }
}