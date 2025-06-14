using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using BulbaLib.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace BulbaLib.Controllers
{
    // [AllowAnonymous] // Removed: Authentication will be required by default or specified per action
    [ApiController]
    [Route("api/[controller]")]
    public class NovelsController : ControllerBase
    {
        private readonly MySqlService _db;
        private readonly IWebHostEnvironment _env;
        private readonly PermissionService _permissionService;

        public NovelsController(MySqlService db, IWebHostEnvironment env, PermissionService permissionService)
        {
            _db = db;
            _env = env;
            _permissionService = permissionService;
        }

        private User GetCurrentUser()
        {
            if (!User.Identity.IsAuthenticated)
            {
                return null;
            }
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
            {
                return null; // Or handle error appropriately
            }
            return _db.GetUser(userId);
        }

        // GET /api/novels?search=...
        [HttpGet]
        [AllowAnonymous] // Explicitly allow anonymous access
        public IActionResult GetNovels([FromQuery] string search = "")
        {
            var novels = _db.GetNovels(search);
            return Ok(novels.Select(n => new {
                id = n.Id,
                title = n.Title,
                description = n.Description,
                covers = n.CoversList, // массив ссылок
                genres = n.Genres,
                tags = n.Tags,
                type = n.Type,
                format = n.Format,
                releaseYear = n.ReleaseYear,
                authorId = n.AuthorId,
                translatorId = n.TranslatorId,
                alternativeTitles = n.AlternativeTitles,
                relatedNovelIds = n.RelatedNovelIds,
                date = n.Date // <<<<<< ДОБАВЛЕНО для фронта!
            }));
        }

        // GET /api/novels/{id}
        [HttpGet("{id}")]
        [AllowAnonymous] // Explicitly allow anonymous access
        public IActionResult GetNovel(int id)
        {
            var novel = _db.GetNovel(id);
            if (novel == null)
                return NotFound(new { error = "Новелла не найдена" });

            var currentUser = GetCurrentUser();
            // For API, we might not use ViewData directly, but the permission values could be returned in the response
            bool canEdit = currentUser != null && _permissionService.CanEditNovel(currentUser, novel);
            bool canDelete = currentUser != null && _permissionService.CanDeleteNovel(currentUser, novel);

            // Example of adding to ViewBag for server-side rendering (though this is an API controller)

            var chapters = _db.GetChaptersByNovel(id) ?? new List<Chapter>();
            int chapterCount = chapters.Count();

            HashSet<int> bookmarkedChapters = null;
            int? bookmarkChapterId = null;

            // Если пользователь авторизован, получаем его закладки
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
                var bookmarks = _db.GetBookmarks(userId);
                if (bookmarks != null && bookmarks.ContainsKey(id.ToString()))
                {
                    bookmarkedChapters = bookmarks[id.ToString()].Select(b => b.ChapterId).ToHashSet();
                    bookmarkChapterId = bookmarks[id.ToString()]
                        .OrderByDescending(b => b.Date)
                        .Select(b => (int?)b.ChapterId)
                        .FirstOrDefault();
                }
            }

            var author = novel.AuthorId.HasValue ? _db.GetUser(novel.AuthorId.Value) : null;

            // --- Переводчики как список объектов {id, login}
            List<dynamic> translatorsList = new List<dynamic>();
            if (!string.IsNullOrWhiteSpace(novel.TranslatorId))
            {
                var ids = novel.TranslatorId.Split(',', System.StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .Where(s => int.TryParse(s, out _))
                                .Select(int.Parse);
                foreach (var uid in ids)
                {
                    var tr = _db.GetUser(uid);
                    if (tr != null)
                        translatorsList.Add(new { id = tr.Id, login = tr.Login });
                }
            }

            var chaptersResult = chapters.Select(ch => new {
                id = ch.Id,
                novelId = ch.NovelId,
                number = ch.Number,
                title = ch.Title,
                content = ch.Content,
                date = ch.Date,
                bookmarked = bookmarkedChapters != null && bookmarkedChapters.Contains(ch.Id)
            }).ToList();

            return Ok(new
            {
                id = novel.Id,
                title = novel.Title,
                description = novel.Description,
                covers = novel.CoversList, // массив ссылок
                genres = novel.Genres,
                tags = novel.Tags,
                type = novel.Type,
                format = novel.Format,
                releaseYear = novel.ReleaseYear,
                chapterCount,
                author = author != null ? new { id = author.Id, login = author.Login } : null,
                alternativeTitles = novel.AlternativeTitles,
                translators = translatorsList,
                chapters = chaptersResult,
                relatedNovelIds = novel.RelatedNovelIds,
                bookmarkChapterId = bookmarkChapterId,
                date = novel.Date // <<<<<< ДОБАВЛЕНО для фронта!
            });
        }

        // POST /api/novels
        [HttpPost]
        [Authorize(Roles = "Admin,Author")] // Require Admin or Author role
        public IActionResult CreateNovel([FromBody] NovelCreateRequest req)
        {
            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                return Unauthorized(); // Should be caught by [Authorize] but good practice
            }

            // Permission check: CanAddNovelDirectly OR CanSubmitNovelForModeration
            // For an API, we might simplify this. If CanAddNovelDirectly, it's direct.
            // If CanSubmitNovelForModeration, the status of the novel should be 'PendingModeration'.
            // This example assumes direct creation if allowed.
            if (!_permissionService.CanAddNovelDirectly(currentUser) && !_permissionService.CanSubmitNovelForModeration(currentUser))
            {
                return Forbid();
            }

            var novel = new Novel
            {
                Title = req.Title,
                Description = req.Description,
                CoversList = req.Covers,
                Genres = req.Genres,
                Tags = req.Tags,
                Type = req.Type,
                Format = req.Format,
                ReleaseYear = req.ReleaseYear,
                AuthorId = req.AuthorId,
                TranslatorId = req.TranslatorId,
                AlternativeTitles = req.AlternativeTitles,
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds() // гарантируем заполнение даты
            };
            _db.CreateNovel(novel);
            return StatusCode(201, new { message = "Novel created" });
        }

        // PUT /api/novels/{id}
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin,Author")] // Require Admin or Author role
        public IActionResult UpdateNovel(int id, [FromBody] NovelUpdateRequest req)
        {
            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                return Unauthorized();
            }

            var novel = _db.GetNovel(id);
            if (novel == null)
                return NotFound(new { error = "Novel not found" });

            if (!_permissionService.CanEditNovel(currentUser, novel))
            {
                return Forbid();
            }

            novel.Title = req.Title ?? novel.Title;
            novel.Description = req.Description ?? novel.Description;
            if (req.Covers != null) novel.CoversList = req.Covers;
            novel.Genres = req.Genres ?? novel.Genres;
            novel.Tags = req.Tags ?? novel.Tags;
            novel.Type = req.Type ?? novel.Type;
            novel.Format = req.Format ?? novel.Format;
            novel.ReleaseYear = req.ReleaseYear ?? novel.ReleaseYear;
            novel.AuthorId = req.AuthorId ?? novel.AuthorId;
            novel.TranslatorId = req.TranslatorId ?? novel.TranslatorId;
            novel.AlternativeTitles = req.AlternativeTitles ?? novel.AlternativeTitles;
            // не обновляем дату при редактировании, только при создании

            _db.UpdateNovel(novel);
            return Ok(new { message = "Novel updated" });
        }

        // DELETE /api/novels/{id}
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,Author")] // Require Admin or Author role
        public IActionResult DeleteNovel(int id)
        {
            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                return Unauthorized();
            }

            var novel = _db.GetNovel(id);
            if (novel == null)
                return NotFound(new { error = "Novel not found" });

            if (!_permissionService.CanDeleteNovel(currentUser, novel))
            {
                return Forbid();
            }

            _db.DeleteNovel(id);
            return Ok(new { message = "Novel deleted" });
        }
    }

    // DTOs для создания/обновления
    public class NovelCreateRequest
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public List<string> Covers { get; set; } // список ссылок
        public string Genres { get; set; }
        public string Tags { get; set; }
        public string Type { get; set; }
        public string Format { get; set; }
        public int? ReleaseYear { get; set; }
        public int? AuthorId { get; set; }
        public string TranslatorId { get; set; }
        public string AlternativeTitles { get; set; }
    }
    public class NovelUpdateRequest
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public List<string> Covers { get; set; }
        public string Genres { get; set; }
        public string Tags { get; set; }
        public string Type { get; set; }
        public string Format { get; set; }
        public int? ReleaseYear { get; set; }
        public int? AuthorId { get; set; }
        public string TranslatorId { get; set; }
        public string AlternativeTitles { get; set; }
    }
}