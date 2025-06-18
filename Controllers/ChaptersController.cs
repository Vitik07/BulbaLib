using BulbaLib.Services;
using Microsoft.AspNetCore.Mvc;
using BulbaLib.Models; // Added
using System.Security.Claims; // Added
using Microsoft.AspNetCore.Authorization; // Added
using System.IO; // For Path
using System.Threading.Tasks; // For Task
using Microsoft.AspNetCore.Http; // For IFormFile
using System; // For DateTimeOffset, Guid
using System.Collections.Generic; // For List
using System.Linq; // For Select
using System.Text.Json; // Added for Moderation

namespace BulbaLib.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChaptersController : Controller
    {
        private readonly MySqlService _db;
        private readonly PermissionService _permissionService; // Added

        public ChaptersController(MySqlService db, PermissionService permissionService) // Modified
        {
            _db = db;
            _permissionService = permissionService; // Added
        }

        private User GetCurrentUser() // Added
        {
            if (!User.Identity.IsAuthenticated)
            {
                return null;
            }
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
            {
                return null;
            }
            return _db.GetUser(userId);
        }

        // GET /api/chapters?novelId=... или /api/chapters?all=1
        [HttpGet]
        [AllowAnonymous] // Explicitly allow anonymous access
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
        [AllowAnonymous] // Explicitly allow anonymous access
        public IActionResult GetChapter(int id)
        {
            var chapter = _db.GetChapter(id);
            if (chapter == null)
                return NotFound(new { error = "Глава не найдена" });

            var novel = _db.GetNovel(chapter.NovelId);
            if (novel == null)
            {
                // This case should ideally not happen if data integrity is maintained
                return NotFound(new { error = "Родительская новелла для главы не найдена" });
            }

            var currentUser = GetCurrentUser();
            bool canEdit = currentUser != null && _permissionService.CanEditChapter(currentUser, chapter, novel);
            bool canDelete = currentUser != null && _permissionService.CanDeleteChapter(currentUser, chapter, novel);

            ViewData["CanEditChapter"] = canEdit;
            ViewData["CanDeleteChapter"] = canDelete;

            return Ok(new
            {
                id = chapter.Id,
                novelId = chapter.NovelId,
                number = chapter.Number,
                title = chapter.Title,
                content = chapter.Content, // Use chapter.Content directly
                date = chapter.Date
            });
        }

        // POST /api/chapters
        [HttpPost]
        [Authorize(Roles = "Admin,Translator")]
        public IActionResult CreateChapter([FromBody] ChapterCreateRequest req)
        {
            if (req.NovelId == 0 || string.IsNullOrWhiteSpace(req.Number) || string.IsNullOrWhiteSpace(req.Title))
                return BadRequest(new { error = "NovelId, number и title обязательны" });

            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                return Unauthorized();
            }

            var novel = _db.GetNovel(req.NovelId);
            if (novel == null)
            {
                return BadRequest(new { error = "Новелла для добавления главы не найдена" });
            }

            // MODIFIED FOR MODERATION
            if (currentUser.Role == UserRole.Translator && _permissionService.CanSubmitChapterForModeration(currentUser, novel))
            {
                var chapterDataForModeration = new Chapter
                {
                    NovelId = req.NovelId,
                    Number = req.Number,
                    Title = req.Title,
                    Content = req.Content ?? "",
                    Date = req.Date ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    CreatorId = currentUser.Id // Translator creating the chapter is the creator
                };

                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.AddChapter,
                    UserId = currentUser.Id,
                    NovelId = req.NovelId,
                    ChapterId = null,
                    RequestData = JsonSerializer.Serialize(chapterDataForModeration),
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.CreateModerationRequest(moderationRequest);
                return Accepted(new { message = "Chapter creation request submitted for moderation." });
            }
            else if (_permissionService.CanAddChapterDirectly(currentUser)) // e.g. Admin
            {
                var chapter = new Chapter
                {
                    NovelId = req.NovelId,
                    Number = req.Number,
                    Title = req.Title,
                    Content = req.Content ?? "",
                    Date = req.Date ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                chapter.CreatorId = currentUser.Id; // Set CreatorId
                _db.CreateChapter(chapter);
                return StatusCode(201, new { message = "Chapter created directly." });
            }
            else
            {
                return Forbid("User not authorized for this action or to bypass moderation.");
            }
        }

        // PUT /api/chapters/{id}
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin,Translator")]
        public IActionResult UpdateChapter(int id, [FromBody] ChapterUpdateRequest req)
        {
            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                return Unauthorized();
            }

            var chapter = _db.GetChapter(id);
            if (chapter == null)
                return NotFound(new { error = "Chapter not found" });

            var novel = _db.GetNovel(chapter.NovelId);
            if (novel == null)
            {
                // This case should ideally not happen if data integrity is maintained
                return NotFound(new { error = "Родительская новелла для главы не найдена" });
            }

            // MODIFIED FOR MODERATION
            if (currentUser.Role == UserRole.Translator && chapter.CreatorId == currentUser.Id && _permissionService.CanSubmitChapterForModeration(currentUser, novel))
            {
                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.EditChapter,
                    UserId = currentUser.Id,
                    NovelId = chapter.NovelId,
                    ChapterId = id,
                    RequestData = JsonSerializer.Serialize(req), // req is ChapterUpdateRequest
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.CreateModerationRequest(moderationRequest);
                return Accepted(new { message = "Chapter update request submitted for moderation." });
            }
            else if (_permissionService.CanEditChapter(currentUser, chapter, novel)) // e.g. Admin
            {
                chapter.Number = req.Number ?? chapter.Number;
                chapter.Title = req.Title ?? chapter.Title;
                chapter.Content = req.Content ?? chapter.Content;
                _db.UpdateChapter(chapter);
                return Ok(new { message = "Chapter updated directly." });
            }
            else
            {
                return Forbid("User not authorized for this action or to bypass moderation.");
            }
        }

        // DELETE /api/chapters/{id}
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,Translator")]
        public IActionResult DeleteChapter(int id)
        {
            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                return Unauthorized();
            }

            var chapter = _db.GetChapter(id);
            if (chapter == null)
                return NotFound(new { error = "Chapter not found" });

            var novel = _db.GetNovel(chapter.NovelId);
            if (novel == null)
            {
                // This case should ideally not happen if data integrity is maintained
                return NotFound(new { error = "Родительская новелла для главы не найдена" });
            }

            // MODIFIED FOR MODERATION
            if (currentUser.Role == UserRole.Translator && chapter.CreatorId == currentUser.Id && _permissionService.CanSubmitChapterForModeration(currentUser, novel))
            {
                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.DeleteChapter,
                    UserId = currentUser.Id,
                    NovelId = chapter.NovelId,
                    ChapterId = id,
                    RequestData = JsonSerializer.Serialize(new { chapter.Number, chapter.Title }), // Optional info
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.CreateModerationRequest(moderationRequest);
                return Accepted(new { message = "Chapter deletion request submitted for moderation." });
            }
            else if (_permissionService.CanDeleteChapter(currentUser, chapter, novel)) // e.g. Admin
            {
                _db.DeleteChapter(id);
                return Ok(new { message = "Chapter deleted directly." });
            }
            else
            {
                return Forbid("User not authorized for this action or to bypass moderation.");
            }
        }

        [HttpPost("{chapterId}/upload-image")]
        [Authorize(Roles = "Admin,Translator")] // Also protect image uploads
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