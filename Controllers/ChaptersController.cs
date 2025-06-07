using BulbaLib.Services;
using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChaptersController : ControllerBase
    {
        private readonly SqliteService _db;

        public ChaptersController(SqliteService db)
        {
            _db = db;
        }

        // GET /api/chapters?novelId=...
        [HttpGet]
        public IActionResult GetChapters([FromQuery] int novelId)
        {
            if (novelId == 0)
                return BadRequest(new { error = "novelId обязателен" });

            var chapters = _db.GetChaptersByNovel(novelId);
            if (chapters == null || chapters.Count == 0)
                return NotFound(new { error = "Главы не найдены" });

            return Ok(new
            {
                chapters = chapters.Select(ch => new
                {
                    id = ch.Id,
                    novelId = ch.NovelId,
                    number = ch.Number,
                    title = ch.Title,
                    content = ch.Content,
                    date = ch.Date
                })
            });
        }

        // GET /api/chapters/{id}
        [HttpGet("{id}")]
        public IActionResult GetChapter(int id)
        {
            var chapter = _db.GetChapter(id);
            if (chapter == null)
                return NotFound(new { error = "Глава не найдена" });
            return Ok(new
            {
                id = chapter.Id,
                novelId = chapter.NovelId,
                number = chapter.Number,
                title = chapter.Title,
                content = chapter.Content,
                date = chapter.Date
            });
        }

        // POST /api/chapters
        [HttpPost]
        public IActionResult CreateChapter([FromBody] ChapterCreateRequest req)
        {
            if (req.NovelId == 0 || req.Number == 0 || string.IsNullOrWhiteSpace(req.Title))
                return BadRequest(new { error = "NovelId, number и title обязательны" });

            var chapter = new Chapter
            {
                NovelId = req.NovelId,
                Number = req.Number,
                Title = req.Title,
                Content = req.Content ?? "",
                Date = req.Date ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            _db.CreateChapter(chapter);
            return StatusCode(201, new { message = "Chapter created" });
        }

        // PUT /api/chapters/{id}
        [HttpPut("{id}")]
        public IActionResult UpdateChapter(int id, [FromBody] ChapterUpdateRequest req)
        {
            var chapter = _db.GetChapter(id);
            if (chapter == null)
                return NotFound(new { error = "Chapter not found" });

            chapter.Number = req.Number ?? chapter.Number;
            chapter.Title = req.Title ?? chapter.Title;
            chapter.Content = req.Content ?? chapter.Content;
            // Date не обновляем специально (оставляем оригинал)
            _db.UpdateChapter(chapter);
            return Ok(new { message = "Chapter updated" });
        }

        // DELETE /api/chapters/{id}
        [HttpDelete("{id}")]
        public IActionResult DeleteChapter(int id)
        {
            _db.DeleteChapter(id);
            return Ok(new { message = "Chapter deleted" });
        }
    }

    // DTOs для создания/обновления главы
    public class ChapterCreateRequest
    {
        public int NovelId { get; set; }
        public int Number { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public long? Date { get; set; }
    }
    public class ChapterUpdateRequest
    {
        public int? Number { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
    }
}