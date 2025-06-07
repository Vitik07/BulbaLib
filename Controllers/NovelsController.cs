using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;
using System.IO;

namespace BulbaLib.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NovelsController : ControllerBase
    {
        private readonly SqliteService _db;
        private readonly IWebHostEnvironment _env;

        public NovelsController(SqliteService db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // GET /api/novels?search=...
        [HttpGet]
        public IActionResult GetNovels([FromQuery] string search = "")
        {
            var novels = _db.GetNovels(search);
            // Для фронта: image отдаём как base64 (или null)
            return Ok(novels.Select(n => new {
                id = n.Id,
                title = n.Title,
                description = n.Description,
                image = n.Image != null ? Convert.ToBase64String(n.Image) : null,
                genres = n.Genres,
                tags = n.Tags
            }));
        }

        // GET /api/novels/{id}
        [HttpGet("{id}")]
        public IActionResult GetNovel(int id, [FromQuery] int? userId = null)
        {
            var novel = _db.GetNovel(id);
            if (novel == null)
                return NotFound(new { error = "Новелла не найдена" });

            var chapters = _db.GetChaptersByNovel(id);
            // Можно добавить тут userId для отметки bookmarked (если потребуется)

            return Ok(new
            {
                id = novel.Id,
                title = novel.Title,
                description = novel.Description,
                image = novel.Image != null ? Convert.ToBase64String(novel.Image) : null,
                genres = novel.Genres,
                tags = novel.Tags,
                chapters = chapters.Select(ch => new {
                    id = ch.Id,
                    novelId = ch.NovelId,
                    number = ch.Number,
                    title = ch.Title,
                    content = ch.Content,
                    date = ch.Date
                })
            });
        }

        // GET /api/novels/{id}/image
        [HttpGet("{id}/image")]
        public IActionResult GetNovelImage(int id)
        {
            var novel = _db.GetNovel(id);
            if (novel == null || novel.Image == null)
                return NotFound(new { error = "Image not found" });
            // По умолчанию jpeg. Если другой формат, поменяй content-type
            return File(novel.Image, "image/jpeg");
        }

        // POST /api/novels
        [HttpPost]
        public IActionResult CreateNovel([FromForm] NovelCreateRequest req)
        {
            var imageBytes = req.Image != null ? ReadBytes(req.Image) : null;
            var novel = new Novel
            {
                Title = req.Title,
                Description = req.Description,
                Image = imageBytes,
                Genres = req.Genres,
                Tags = req.Tags
            };
            _db.CreateNovel(novel);
            return StatusCode(201, new { message = "Novel created" });
        }

        // PUT /api/novels/{id}
        [HttpPut("{id}")]
        public IActionResult UpdateNovel(int id, [FromBody] NovelUpdateRequest req)
        {
            var novel = _db.GetNovel(id);
            if (novel == null)
                return NotFound(new { error = "Novel not found" });
            novel.Title = req.Title ?? novel.Title;
            novel.Description = req.Description ?? novel.Description;
            novel.Genres = req.Genres ?? novel.Genres;
            novel.Tags = req.Tags ?? novel.Tags;
            // Для Image можно реализовать обновление через отдельный endpoint или через base64 строку
            _db.UpdateNovel(novel);
            return Ok(new { message = "Novel updated" });
        }

        // DELETE /api/novels/{id}
        [HttpDelete("{id}")]
        public IActionResult DeleteNovel(int id)
        {
            _db.DeleteNovel(id);
            return Ok(new { message = "Novel deleted" });
        }

        private byte[] ReadBytes(IFormFile file)
        {
            using var ms = new MemoryStream();
            file.CopyTo(ms);
            return ms.ToArray();
        }
    }

    // DTOs для создания/обновления
    public class NovelCreateRequest
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public IFormFile Image { get; set; }
        public string Genres { get; set; }
        public string Tags { get; set; }
    }
    public class NovelUpdateRequest
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Genres { get; set; }
        public string Tags { get; set; }
        // Для Image можно добавить base64, если нужно
    }
}