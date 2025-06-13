using BulbaLib.Services;
using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChaptersController : ControllerBase
    {
        private readonly MySqlService _db;

        public ChaptersController(MySqlService db)
        {
            _db = db;
        }

        // GET /api/chapters?novelId=... или /api/chapters?all=1
        [HttpGet]
        public IActionResult GetChapters([FromQuery] int novelId = 0, [FromQuery] int all = 0)
        {
            // Новый блок: если all=1 — вернуть все главы всех новелл
            if (all == 1)
            {
                var novels = _db.GetNovels();
                var allChapters = new List<Chapter>();
                foreach (var novel in novels)
                    allChapters.AddRange(_db.GetChaptersByNovel(novel.Id));

                return Ok(new
                {
                    chapters = allChapters.Select(ch => new
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

            // Обычный режим — главы конкретной новеллы
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

            string chapterText = "";
            if (!string.IsNullOrEmpty(chapter.Content))
            {
                var wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var filePath = Path.Combine(wwwroot, chapter.Content.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(filePath))
                {
                    chapterText = System.IO.File.ReadAllText(filePath);
                }
                else
                {
                    chapterText = "[Текст главы не найден: " + filePath + "]";
                }
            }

            return Ok(new
            {
                id = chapter.Id,
                novelId = chapter.NovelId,
                number = chapter.Number,
                title = chapter.Title,
                content = chapterText,
                date = chapter.Date
            });
        }

        // POST /api/chapters
        [HttpPost]
        public IActionResult CreateChapter([FromBody] ChapterCreateRequest req)
        {
            if (req.NovelId == 0 || string.IsNullOrWhiteSpace(req.Number) || string.IsNullOrWhiteSpace(req.Title))
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

        [HttpPost("{chapterId}/upload-image")]
        public async Task<IActionResult> UploadImage(int chapterId, IFormFile image)
        {
            if (image == null || image.Length == 0)
                return BadRequest(new { error = "Файл не выбран" });

            var uploadDir = Path.Combine("wwwroot", "uploads", "chapters", chapterId.ToString());
            Directory.CreateDirectory(uploadDir);
            var fileName = Guid.NewGuid() + Path.GetExtension(image.FileName);
            var filePath = Path.Combine(uploadDir, fileName);

            using (var stream = System.IO.File.Create(filePath))
            {
                await image.CopyToAsync(stream);
            }
            var url = $"/uploads/chapters/{chapterId}/{fileName}";
            return Ok(new { url });
        }
    }

    // DTOs для создания/обновления главы
    public class ChapterCreateRequest
    {
        public int NovelId { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public long? Date { get; set; }
    }
    public class ChapterUpdateRequest
    {
        public string Number { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
    }
}