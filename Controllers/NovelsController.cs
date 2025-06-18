using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using BulbaLib.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Text.Json; // Added for moderation
using System; // Added for DateTime

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
            // MODIFIED FOR MODERATION
            if (currentUser.Role == UserRole.Author)
            {
                if (!_permissionService.CanSubmitNovelForModeration(currentUser))
                {
                    return Forbid("Authors are not allowed to submit novels for moderation based on current permissions.");
                }

                var novelDataForModeration = new Novel
                {
                    Title = req.Title,
                    Description = req.Description,
                    CoversList = req.Covers,
                    Genres = req.Genres,
                    Tags = req.Tags,
                    Type = req.Type,
                    Format = req.Format,
                    ReleaseYear = req.ReleaseYear,
                    AuthorId = currentUser.Id, // Author creating the novel is the author
                    AlternativeTitles = req.AlternativeTitles,
                    Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.NovelCreate,
                    UserId = currentUser.Id,
                    NovelId = null,
                    RequestData = JsonSerializer.Serialize(novelDataForModeration),
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.CreateModerationRequest(moderationRequest);
                return Accepted(new { message = "Novel creation request submitted for moderation." });
            }
            else if (currentUser.Role == UserRole.Admin) // Assuming Admin can create directly
            {
                if (!_permissionService.CanAddNovelDirectly(currentUser)) // Check if admin actually has direct add permission
                {
                    return Forbid("Admins are not allowed to add novels directly based on current permissions.");
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
                    AuthorId = req.AuthorId ?? currentUser.Id, // Admin can specify AuthorId, defaults to self
                    AlternativeTitles = req.AlternativeTitles,
                    Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                _db.CreateNovel(novel);
                return StatusCode(201, new { message = "Novel created directly by Admin." });
            }
            else
            {
                return Forbid("User role not authorized for this action.");
            }
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

            // MODIFIED FOR MODERATION
            if (currentUser.Role == UserRole.Author)
            {
                if (novel.AuthorId != currentUser.Id)
                {
                    return Forbid("Authors can only request updates for their own novels.");
                }
                if (!_permissionService.CanSubmitNovelForModeration(currentUser))
                {
                    return Forbid("Authors are not allowed to submit novel updates for moderation based on current permissions.");
                }

                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.NovelUpdate,
                    UserId = currentUser.Id,
                    NovelId = id,
                    RequestData = JsonSerializer.Serialize(req), // req is NovelUpdateRequest
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.CreateModerationRequest(moderationRequest);
                return Accepted(new { message = "Novel update request submitted for moderation." });
            }
            else if (currentUser.Role == UserRole.Admin) // Assuming Admin can update directly
            {
                if (!_permissionService.CanEditNovel(currentUser, novel)) // Check if admin actually has direct edit permission
                {
                    return Forbid("Admins are not allowed to edit this novel directly based on current permissions.");
                }
                novel.Title = req.Title ?? novel.Title;
                novel.Description = req.Description ?? novel.Description;
                if (req.Covers != null) novel.CoversList = req.Covers;
                novel.Genres = req.Genres ?? novel.Genres;
                novel.Tags = req.Tags ?? novel.Tags;
                novel.Type = req.Type ?? novel.Type;
                novel.Format = req.Format ?? novel.Format;
                novel.ReleaseYear = req.ReleaseYear ?? novel.ReleaseYear;
                // Admin might be allowed to change AuthorId, if req.AuthorId is part of NovelUpdateRequest and handled
                if (req.AuthorId.HasValue) novel.AuthorId = req.AuthorId;
                novel.AlternativeTitles = req.AlternativeTitles ?? novel.AlternativeTitles;

                _db.UpdateNovel(novel);
                return Ok(new { message = "Novel updated directly by Admin." });
            }
            else
            {
                return Forbid("User role not authorized for this action.");
            }
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

            // MODIFIED FOR MODERATION
            if (currentUser.Role == UserRole.Author)
            {
                if (novel.AuthorId != currentUser.Id)
                {
                    return Forbid("Authors can only request deletion for their own novels.");
                }
                if (!_permissionService.CanSubmitNovelForModeration(currentUser))
                {
                    return Forbid("Authors are not allowed to submit novel deletions for moderation based on current permissions.");
                }

                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.NovelDelete,
                    UserId = currentUser.Id,
                    NovelId = id,
                    RequestData = JsonSerializer.Serialize(new { novel.Title }), // Optional info
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.CreateModerationRequest(moderationRequest);
                return Accepted(new { message = "Novel deletion request submitted for moderation." });
            }
            else if (currentUser.Role == UserRole.Admin) // Assuming Admin can delete directly
            {
                if (!_permissionService.CanDeleteNovel(currentUser, novel)) // Check if admin actually has direct delete permission
                {
                    return Forbid("Admins are not allowed to delete this novel directly based on current permissions.");
                }
                _db.DeleteNovel(id);
                return Ok(new { message = "Novel deleted directly by Admin." });
            }
            else
            {
                return Forbid("User role not authorized for this action.");
            }
        }

        private string? GetFirstCover(string? coversJson)
        {
            if (string.IsNullOrWhiteSpace(coversJson))
            {
                return null;
            }
            try
            {
                var coversList = System.Text.Json.JsonSerializer.Deserialize<List<string>>(coversJson);
                return coversList?.FirstOrDefault();
            }
            catch (System.Text.Json.JsonException)
            {
                // Optional: Log error (_logger would need to be injected into this controller)
                // For now, returning null on parsing error.
                return null;
            }
        }

        [HttpGet("search")] // Will be routed as /api/Novels/search
        [AllowAnonymous]    // Assuming search should be public
        public IActionResult SearchNovelsApi([FromQuery] string query, [FromQuery] int limit = 5)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Search query cannot be empty.");
            }
            if (limit <= 0) limit = 5; // Default limit if invalid
            if (limit > 20) limit = 20; // Max limit

            var novelsFromDb = _db.SearchNovelsByTitle(query, limit);

            var results = novelsFromDb.Select(novel => new
            {
                novel.Id,
                novel.Title,
                FirstCoverUrl = GetFirstCover(novel.Covers)
            }).ToList();

            return Ok(results);
        }

        // Add using System.Linq; if not already present
        // Add using BulbaLib.Models; if not already present

        [HttpGet("detailsByIds")]
        public IActionResult GetNovelDetailsByIds([FromQuery] string ids)
        {
            if (string.IsNullOrWhiteSpace(ids))
            {
                return BadRequest("IDs cannot be empty.");
            }

            var idList = new List<int>();
            try
            {
                idList = ids.Split(',').Select(int.Parse).ToList();
            }
            catch (FormatException)
            {
                return BadRequest("Invalid ID format. IDs should be comma-separated integers.");
            }

            if (!idList.Any())
            {
                return Ok(new List<object>()); // Return empty list if no valid IDs parsed, though split would likely yield one empty string then fail int.Parse
            }

            // Assuming _mySqlService can fetch multiple novels by IDs.
            // If not, this needs to be implemented in MySqlService.
            // For now, let's assume a method GetNovelsByIds exists or iterate.
            var novels = _db.GetNovelsByIds(idList); // This method needs to exist in MySqlService

            if (novels == null || !novels.Any())
            {
                return NotFound("No novels found for the provided IDs.");
            }

            // Select only needed data to prevent over-fetching
            var result = novels.Select(n => new {
                n.Id,
                n.Title,
                FirstCoverUrl = !string.IsNullOrWhiteSpace(n.Covers) ?
                                (System.Text.Json.JsonSerializer.Deserialize<List<string>>(n.Covers)?.FirstOrDefault()) :
                                null
            }).ToList();

            return Ok(result);
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
        public string AlternativeTitles { get; set; }
    }
}