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
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging; // Added for ILogger
using BulbaLib.Services; // Replaced BulbaLib.Interfaces
// Ensure FileService is in BulbaLib.Services
// using Microsoft.AspNetCore.Hosting; // Not directly needed if FileService handles it
using System.ComponentModel.DataAnnotations; // Added for ViewModels
using System.Text.RegularExpressions; // Added for Regex

namespace BulbaLib.Controllers
{
    // [ApiController] // Removed
    // [Route("api/[controller]")] // Removed
    public class ChaptersController : Controller
    {
        private readonly MySqlService _mySqlService;
        private readonly FileService _fileService;
        private readonly PermissionService _permissionService;
        private readonly ICurrentUserService _currentUserService;
        private readonly INotificationService _notificationService;
        private readonly ILogger<ChaptersController> _logger;

        public ChaptersController(
            MySqlService mySqlService,
            FileService fileService,
            PermissionService permissionService,
            ICurrentUserService currentUserService,
            INotificationService notificationService,
            ILogger<ChaptersController> logger)
        {
            _mySqlService = mySqlService;
            _fileService = fileService;
            _permissionService = permissionService;
            _currentUserService = currentUserService;
            _notificationService = notificationService;
            _logger = logger;
        }

        private User GetCurrentUser()
        {
            return _currentUserService.GetCurrentUser();
        }

        // Helper method for path construction (can be private in controller or moved to FileService if more generic)
        private string ConstructChapterPath(Chapter chapter)
        {
            // This logic should ideally mirror exactly how FileService *saves* paths if ContentFilePath is missing.
            // This is a replication of the logic from previous version of Edit GET.
            // Regex for sanitizing parts of path, removing potentially problematic characters or sequences.
            // Allowing: letters, numbers, spaces, dots, hyphens, underscores. Replacing others with underscore.
            // This is a simplified version. Consider a more robust path sanitization/generation within FileService.
            string invalidPathCharsPattern = @"[^\w\s\.\-_]"; // \w includes letters, numbers, underscore

            string sanitizedNumber = string.IsNullOrWhiteSpace(chapter.Number) ? "Chapter" : Regex.Replace(chapter.Number, invalidPathCharsPattern, "_", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            string sanitizedTitle = string.IsNullOrWhiteSpace(chapter.Title) ? "Untitled" : Regex.Replace(chapter.Title, invalidPathCharsPattern, "_", RegexOptions.None, TimeSpan.FromMilliseconds(100));

            // Further trim and ensure not overly long
            sanitizedNumber = sanitizedNumber.Length > 50 ? sanitizedNumber.Substring(0, 50).Trim('_') : sanitizedNumber.Trim('_');
            sanitizedTitle = sanitizedTitle.Length > 50 ? sanitizedTitle.Substring(0, 50).Trim('_') : sanitizedTitle.Trim('_');

            // Replace common path problematic chars that might have slipped through or formed by replacements
            char[] generalInvalidChars = Path.GetInvalidFileNameChars();
            sanitizedNumber = string.Join("_", sanitizedNumber.Split(generalInvalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim('_');
            sanitizedTitle = string.Join("_", sanitizedTitle.Split(generalInvalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim('_');

            string fileName;
            bool numIsEmpty = string.IsNullOrWhiteSpace(sanitizedNumber) || sanitizedNumber == "_" || string.Equals(sanitizedNumber, "Chapter", StringComparison.OrdinalIgnoreCase);
            bool titleIsEmpty = string.IsNullOrWhiteSpace(sanitizedTitle) || sanitizedTitle == "_" || string.Equals(sanitizedTitle, "Untitled", StringComparison.OrdinalIgnoreCase);

            if (numIsEmpty && titleIsEmpty) fileName = "Глава_без_номера_и_названия"; // Ensure filename is valid
            else if (titleIsEmpty) fileName = $"{sanitizedNumber}";
            else if (numIsEmpty) fileName = $"{sanitizedTitle}";
            else fileName = $"{sanitizedNumber}-{sanitizedTitle}"; // Using hyphen as separator

            // Final sanitization for the filename itself (again, to be safe)
            fileName = string.Join("_", fileName.Split(generalInvalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim('_');

            if (string.IsNullOrWhiteSpace(fileName)) fileName = "DefaultChapterName";

            fileName += ".txt";

            // Path.Combine is good for platform-agnostic paths.
            // The FileService should handle the root path (e.g., wwwroot) internally.
            // This relative path is what would be stored or used by FileService.
            string relativePath = Path.Combine("uploads", "content", chapter.NovelId.ToString(), fileName);

            // Ensure it's a relative path starting with / if that's the convention used by FileService
            // However, FileService methods like ReadChapterContentAsync should ideally handle joining with base path correctly.
            // If FileService expects paths starting with "/", this ensures it.
            // if (!relativePath.StartsWith("/") && !Path.IsPathRooted(relativePath)) relativePath = "/" + relativePath;
            // For now, returning the combined path directly, assuming FileService handles it.
            return relativePath;
        }

        // GET: Chapter/Create
        [HttpGet]
        [Authorize(Roles = "Translator,Admin")]
        public IActionResult Create(int novelId)
        {
            var novel = _mySqlService.GetNovel(novelId);
            if (novel == null)
            {
                TempData["ErrorMessage"] = "Новелла не найдена.";
                return RedirectToAction("Index", "CatalogView"); // Or other appropriate redirect
            }

            var currentUser = _currentUserService.GetCurrentUser(); // Assuming GetCurrentUser() is more appropriate than User.FindFirstValue
            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "Пожалуйста, войдите в систему.";
                return RedirectToAction("Login", "AuthView");
            }

            if (!_permissionService.CanCreateChapter(currentUser, novel))
            {
                TempData["ErrorMessage"] = "У вас нет прав на добавление главы к этой новелле.";
                // Consider RedirectToAction to an error page or the novel page itself
                return Forbid();
            }

            var model = new ChapterCreateModel { NovelId = novelId, NovelTitle = novel.Title };
            return View("~/Views/Chapter/Create.cshtml", model);
        }

        [HttpPost]
        [Authorize(Roles = "Translator,Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ChapterCreateModel model)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var novel = _mySqlService.GetNovel(model.NovelId);

            if (novel == null)
            {
                TempData["ErrorMessage"] = "Новелла не найдена.";
                return RedirectToAction("Index", "CatalogView");
            }
            ViewData["NovelTitle"] = novel.Title;

            bool canProceed = false;
            if (currentUser != null)
            {
                if (currentUser.Role == UserRole.Admin) canProceed = _permissionService.CanAddChapterDirectly(currentUser);
                else if (currentUser.Role == UserRole.Translator) canProceed = _permissionService.CanSubmitChapterForModeration(currentUser, novel);
            }

            if (!canProceed)
            {
                TempData["ErrorMessage"] = "У вас нет прав для выполнения этого действия.";
                return RedirectToAction("Novel", "NovelView", new { id = model.NovelId });
            }

            if (!ModelState.IsValid)
            {
                return View("~/Views/Chapter/Create.cshtml", model);
            }

            var chapter = new Chapter
            {
                NovelId = model.NovelId,
                Number = model.Number,
                Title = model.Title,
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                CreatorId = currentUser.Id
            };

            if (currentUser.Role == UserRole.Admin)
            {
                string filePath = await _fileService.SaveChapterContentAsync(chapter.NovelId, chapter.Number, chapter.Title, model.Content);
                if (string.IsNullOrEmpty(filePath))
                {
                    ModelState.AddModelError(string.Empty, "Ошибка сохранения содержимого главы.");
                    return View("~/Views/Chapter/Create.cshtml", model);
                }
                // chapter.ContentFilePath = filePath; // If Chapter model has this field
                chapter.ContentFilePath = filePath; // Assign the path
                _mySqlService.CreateChapter(chapter);

                var translators = _mySqlService.GetTranslatorsForNovel(novel.Id);
                if (!translators.Any(t => t.Id == currentUser.Id))
                {
                    _mySqlService.AddNovelTranslator(novel.Id, currentUser.Id);
                }

                TempData["SuccessMessage"] = "Глава успешно добавлена.";
                return RedirectToAction("Novel", "NovelView", new { id = model.NovelId });
            }
            else // UserRole.Translator
            {
                var chapterDataForModeration = new Chapter
                {
                    NovelId = model.NovelId,
                    Number = model.Number,
                    Title = model.Title,
                    Content = model.Content,
                    Date = chapter.Date,
                    CreatorId = currentUser.Id
                };
                var requestDataJson = JsonSerializer.Serialize(chapterDataForModeration);

                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.AddChapter,
                    UserId = currentUser.Id,
                    NovelId = model.NovelId,
                    RequestData = requestDataJson,
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateModerationRequest(moderationRequest);

                TempData["SuccessMessage"] = "Запрос на добавление главы отправлен на модерацию.";
                return RedirectToAction("Novel", "NovelView", new { id = model.NovelId });
            }
        }

        [Authorize(Roles = "Translator,Admin")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id) // chapterId
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "Пожалуйста, войдите в систему.";
                return RedirectToAction("Login", "AuthView");
            }

            var chapter = _mySqlService.GetChapter(id);
            if (chapter == null)
            {
                return NotFound("Глава не найдена.");
            }

            var novel = _mySqlService.GetNovel(chapter.NovelId);
            if (novel == null)
            {
                TempData["ErrorMessage"] = "Родительская новелла не найдена.";
                return RedirectToAction("Index", "CatalogView");
            }

            // Permission check: Can user edit this chapter?
            // This might involve checking if currentUser.Id == chapter.CreatorId for translators
            if (!_permissionService.CanEditChapter(currentUser, chapter, novel) &&
                !(currentUser.Role == UserRole.Translator && chapter.CreatorId == currentUser.Id)) // Explicitly allow self-edit for translator before moderation
            {
                TempData["ErrorMessage"] = "У вас нет прав для редактирования этой главы.";
                return RedirectToAction("Novel", "NovelView", new { id = chapter.NovelId });
            }

            // For Admins or Authors editing their own (pre-moderation, if that state exists for chapters)
            // the content needs to be read from the file.
            // For Translators submitting an edit via moderation, the original content might not be needed on this form,
            // or it is, so they can see what they are editing.
            // The current FileService does not have a way to get file path from chapter details easily.
            // Assuming a temporary convention or that ContentFilePath will be added to Chapter model later.
            // For now, if it's an Admin or the Creator, we'll try to read. Otherwise, content might be empty for an edit proposal.

            string currentContent = string.Empty;
            // This is a placeholder for how content path would be determined.
            // This logic needs to be robust, e.g. using a dedicated field in `Chapter` model or a reliable naming convention.
            // For now, we'll construct a potential path. This is NOT ideal for production.
            // String constructedPath = $"wwwroot/uploads/content/{chapter.NovelId}/{chapter.Number} - {chapter.Title}.txt"; 
            // Instead of relying on constructed path, for now, if user is Admin or creator, we assume they are editing *before* moderation approval,
            // so the content they want to edit is what they will type in. If content was already approved and saved,
            // the Edit action for Admin would re-save it. For Translator, they propose new content.
            // The task states: "прочитать контент главы из файла через _fileService.ReadChapterContentAsync(chapter.ContentFilePath)"
            // This implies chapter.ContentFilePath exists. Since it doesn't, I'll simulate this for Admin for now,
            // and for Translator, they'd be providing new content.

            // Let's assume for an Admin edit, they might want to see the current approved content.
            // This part is tricky without ContentFilePath.
            // I will proceed by NOT loading existing content from file here, assuming FileService will handle it if path is known
            // or that it's not strictly necessary for the Edit form's initial load (user types changes).
            // The POST action will save whatever is submitted.

            // If chapter.ContentFilePath was available:
            // if (!string.IsNullOrEmpty(chapter.ContentFilePath)) {
            //    currentContent = await _fileService.ReadChapterContentAsync(chapter.ContentFilePath) ?? string.Empty;
            // }

            // --- Start of new code for Edit GET ---
            string chapterContent = string.Empty;
            // Construct file path logic from FileService.SaveChapterContentAsync
            // This is a simplified replication. Ideally, FileService would provide a method to get the path.
            // --- Start of new code for Edit GET ---
            if (!string.IsNullOrEmpty(chapter.ContentFilePath))
            {
                if (_fileService.ChapterFileExists(chapter.ContentFilePath)) // Check if file actually exists at the stored path
                {
                    chapterContent = await _fileService.ReadChapterContentAsync(chapter.ContentFilePath) ?? string.Empty;
                }
                else
                {
                    _logger.LogWarning("ContentFilePath is set for chapter {ChapterId} to {Path}, but file not found. Attempting to construct path as fallback.", chapter.Id, chapter.ContentFilePath);
                    // Fallback logic (or remove if strict reliance on ContentFilePath is desired)
                    // For now, keeping fallback might be safer for older data.
                    string constructedPath = ConstructChapterPath(chapter); // Extracted to a helper or use existing logic inline
                    if (_fileService.ChapterFileExists(constructedPath))
                    {
                        chapterContent = await _fileService.ReadChapterContentAsync(constructedPath) ?? string.Empty;
                    }
                    else
                    {
                        _logger.LogWarning("Fallback path construction for chapter {ChapterId} also failed to find file at {Path}.", chapter.Id, constructedPath);
                    }
                }
            }
            else
            {
                _logger.LogWarning("ContentFilePath is missing for chapter {ChapterId}. Attempting to construct path.", chapter.Id);
                // Fallback logic
                string constructedPath = ConstructChapterPath(chapter); // Extracted to a helper or use existing logic inline
                if (_fileService.ChapterFileExists(constructedPath))
                {
                    chapterContent = await _fileService.ReadChapterContentAsync(constructedPath) ?? string.Empty;
                }
                else
                {
                    _logger.LogWarning("Fallback path construction for chapter {ChapterId} failed to find file at {Path}.", chapter.Id, constructedPath);
                }
            }
            // --- End of new code for Edit GET ---

            var model = new ChapterEditModel
            {
                Id = chapter.Id,
                NovelId = chapter.NovelId,
                NovelTitle = novel.Title, // Added NovelTitle to model
                Number = chapter.Number,
                Title = chapter.Title,
                Content = chapterContent // Use content read from file
            };
            // ViewData["NovelTitle"] = novel.Title; // No longer primary way if model has it

            return View("~/Views/Chapter/Edit.cshtml", model);
        }

        [HttpPost]
        [Authorize(Roles = "Translator,Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ChapterEditModel model)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "Пожалуйста, войдите в систему.";
                return RedirectToAction("Login", "AuthView");
            }

            var existingChapter = _mySqlService.GetChapter(model.Id);
            if (existingChapter == null)
            {
                return NotFound("Глава не найдена.");
            }
            // Need the novel for permission checks and context
            var novel = _mySqlService.GetNovel(existingChapter.NovelId);
            if (novel == null)
            {
                TempData["ErrorMessage"] = "Родительская новелла не найдена.";
                return RedirectToAction("Index", "CatalogView");
            }
            ViewData["NovelTitle"] = novel.Title; // Repopulate for view if needed

            // Permission check
            if (!_permissionService.CanEditChapter(currentUser, existingChapter, novel) &&
                !(currentUser.Role == UserRole.Translator && existingChapter.CreatorId == currentUser.Id))
            {
                TempData["ErrorMessage"] = "У вас нет прав для редактирования этой главы.";
                return RedirectToAction("Novel", "NovelView", new { id = existingChapter.NovelId });
            }

            if (!ModelState.IsValid)
            {
                return View("~/Views/Chapter/Edit.cshtml", model);
            }

            // Logic for Admins: Direct edit
            if (currentUser.Role == UserRole.Admin)
            {
                // Construct the presumed old file path based on OLD details if Number/Title could change
                // This is complex if FileService doesn't store/manage paths directly linked to chapter IDs.
                // For simplicity, assume FileService.SaveChapterContentAsync can overwrite or handle this.
                // A more robust solution would involve storing file paths or having FileService manage them by ID.

                // If chapter number or title (which might form the filename) has changed,
                // we might need to delete the old file. This is tricky without a stored ContentFilePath.
                // Let's assume SaveChapterContentAsync handles overwriting correctly if the path is the same,
                // or creates a new file if path components (number/title) change.
                // Deleting the "old" file when its name components change is not straightforward here.

                string oldFilePath = existingChapter.ContentFilePath; // Get existing path BEFORE updating chapter details

                string newFilePath = await _fileService.SaveChapterContentAsync(existingChapter.NovelId, model.Number, model.Title, model.Content);
                if (string.IsNullOrEmpty(newFilePath))
                {
                    ModelState.AddModelError(string.Empty, "Ошибка сохранения содержимого главы.");
                    // Must repopulate NovelTitle for the view model if returning
                    model.NovelTitle = novel.Title; // Or ViewData["NovelTitle"] = novel.Title;
                    return View("~/Views/Chapter/Edit.cshtml", model);
                }

                existingChapter.Number = model.Number;
                existingChapter.Title = model.Title;
                existingChapter.ContentFilePath = newFilePath; // If storing path
                // Content itself is not stored in existingChapter for DB

                _mySqlService.UpdateChapter(existingChapter);

                // Delete old file if path changed and old path existed
                if (!string.IsNullOrEmpty(oldFilePath) && oldFilePath != newFilePath)
                {
                    // Ensure the path is relative to wwwroot or however FileService expects it.
                    // FileService.DeleteChapterContentAsync should handle the actual deletion from the filesystem.
                    // The path stored in ContentFilePath should be the same format that DeleteChapterContentAsync expects.
                    await _fileService.DeleteChapterContentAsync(oldFilePath);
                }

                TempData["SuccessMessage"] = "Глава успешно обновлена.";
                return RedirectToAction("Novel", "NovelView", new { id = existingChapter.NovelId });
            }
            else // UserRole.Translator
            {
                // Translators submit an edit request
                var chapterDataForModeration = new Chapter // Or a specific DTO for update
                {
                    Id = model.Id, // Important for identifying which chapter to update
                    NovelId = existingChapter.NovelId,
                    Number = model.Number,
                    Title = model.Title,
                    Content = model.Content, // New proposed content
                    Date = existingChapter.Date, // Keep original creation date or update? Typically original.
                    CreatorId = existingChapter.CreatorId // Creator does not change
                };
                var requestDataJson = JsonSerializer.Serialize(chapterDataForModeration);

                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.EditChapter,
                    UserId = currentUser.Id,
                    NovelId = existingChapter.NovelId,
                    ChapterId = existingChapter.Id,
                    RequestData = requestDataJson,
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateModerationRequest(moderationRequest);

                TempData["SuccessMessage"] = "Запрос на редактирование главы отправлен на модерацию.";
                return RedirectToAction("Novel", "NovelView", new { id = existingChapter.NovelId });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Translator,Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id) // chapterId
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "Пожалуйста, войдите в систему.";
                return RedirectToAction("Login", "AuthView");
            }

            var chapterToDelete = _mySqlService.GetChapter(id);
            if (chapterToDelete == null)
            {
                return NotFound("Глава не найдена.");
            }
            var novel = _mySqlService.GetNovel(chapterToDelete.NovelId); // Needed for context and permissions
            if (novel == null)
            {
                TempData["ErrorMessage"] = "Родительская новелла не найдена.";
                // Potentially redirect to a general error page or catalog if novel context is lost
                return RedirectToAction("Index", "CatalogView");
            }


            // Permission check
            if (!_permissionService.CanDeleteChapter(currentUser, chapterToDelete, novel) &&
                !(currentUser.Role == UserRole.Translator && chapterToDelete.CreatorId == currentUser.Id))
            {
                TempData["ErrorMessage"] = "У вас нет прав для удаления этой главы.";
                return RedirectToAction("Novel", "NovelView", new { id = chapterToDelete.NovelId });
            }

            if (currentUser.Role == UserRole.Admin)
            {
                // Attempt to delete chapter content file.
                // This is difficult without a stored ContentFilePath in the Chapter model.
                // FileService.DeleteChapterContentAsync would need a path.
                // Placeholder: if a consistent naming scheme was used by SaveChapterContentAsync based on novelId, chapter number, title:
                // string presumedFilePath = $"/uploads/content/{chapterToDelete.NovelId}/{chapterToDelete.Number} - {chapterToDelete.Title}.txt";
                // await _fileService.DeleteChapterContentAsync(presumedFilePath);
                // This is risky if names change or have special characters not handled identically.
                // For now, we'll log that file deletion should occur.
                // _logger.LogInformation("Admin deleting chapter. File content for chapter {ChapterId} (Novel {NovelId}) should be manually reviewed or deleted if path is not stored.", id, chapterToDelete.NovelId);
                // With ContentFilePath, we can attempt to delete it.
                if (!string.IsNullOrEmpty(chapterToDelete.ContentFilePath))
                {
                    await _fileService.DeleteChapterContentAsync(chapterToDelete.ContentFilePath);
                    _logger.LogInformation("Attempted to delete content file {FilePath} for chapter {ChapterId}", chapterToDelete.ContentFilePath, id);
                }
                else
                {
                    _logger.LogWarning("ContentFilePath not found for chapter {ChapterId}. File not deleted.", id);
                }

                _mySqlService.DeleteChapter(id);

                // Check if this was the last chapter by this user for this novel (if user is a translator)
                // This logic is complex and might be better suited for a service layer or when handling moderation response.
                // For direct admin deletion, it's less common for admin to be the "translator" whose status depends on chapter count.
                // However, if an Admin was also acting as a translator:
                if (chapterToDelete.CreatorId.HasValue)
                {
                    var remainingChaptersByCreator = _mySqlService.GetChaptersByNovel(novel.Id)
                                                            .Count(c => c.CreatorId == chapterToDelete.CreatorId.Value);
                    if (remainingChaptersByCreator == 0)
                    {
                        // Check if user is in NovelTranslators before removing
                        var translators = _mySqlService.GetTranslatorsForNovel(novel.Id);
                        if (translators.Any(t => t.Id == chapterToDelete.CreatorId.Value))
                        {
                            _mySqlService.RemoveNovelTranslator(novel.Id, chapterToDelete.CreatorId.Value);
                            _logger.LogInformation("User {UserId} removed from NovelTranslators for Novel {NovelId} as it was their last chapter.", chapterToDelete.CreatorId.Value, novel.Id);
                        }
                    }
                }

                TempData["SuccessMessage"] = "Глава успешно удалена.";
                return RedirectToAction("Novel", "NovelView", new { id = chapterToDelete.NovelId });
            }
            else // UserRole.Translator
            {
                var requestData = JsonSerializer.Serialize(new { Title = chapterToDelete.Title, Number = chapterToDelete.Number, Id = chapterToDelete.Id });
                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.DeleteChapter,
                    UserId = currentUser.Id,
                    NovelId = chapterToDelete.NovelId,
                    ChapterId = chapterToDelete.Id,
                    RequestData = requestData,
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateModerationRequest(moderationRequest);

                TempData["SuccessMessage"] = "Запрос на удаление главы отправлен на модерацию.";
                return RedirectToAction("Novel", "NovelView", new { id = chapterToDelete.NovelId });
            }
        }
        // End MVC Actions for Chapters


        // GET /api/chapters?novelId=... или /api/chapters?all=1
        [HttpGet("api/chapters/list")]
        [AllowAnonymous]
        public IActionResult GetChaptersApi([FromQuery] int novelId = 0, [FromQuery] int all = 0) // Renamed
        {
            // Новый блок: если all=1 — вернуть все главы всех новелл
            if (all == 1)
            {
                var novels = _mySqlService.GetNovels();
                var allChapters = new List<Chapter>();
                foreach (var novel in novels)
                    allChapters.AddRange(_mySqlService.GetChaptersByNovel(novel.Id)); // Corrected

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

            var chapters = _mySqlService.GetChaptersByNovel(novelId); // Corrected
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
        [HttpGet("api/chapters/{id}")]
        [AllowAnonymous]
        public IActionResult GetChapterApi(int id)
        {
            var chapter = _mySqlService.GetChapter(id);
            if (chapter == null)
                return NotFound(new { error = "Глава не найдена" });

            var novelContext = _mySqlService.GetNovel(chapter.NovelId); // Renamed 'novel' to 'novelContext'
            if (novelContext == null)
            {
                // This case should ideally not happen if data integrity is maintained
                return NotFound(new { error = "Родительская новелла для главы не найдена" });
            }

            var currentUser = GetCurrentUser();
            bool canEdit = currentUser != null && _permissionService.CanEditChapter(currentUser, chapter, novelContext); // Use novelContext
            bool canDelete = currentUser != null && _permissionService.CanDeleteChapter(currentUser, chapter, novelContext); // Use novelContext

            ViewData["CanEditChapter"] = canEdit;
            ViewData["CanDeleteChapter"] = canDelete;

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
        [HttpPost("api/chapters")]
        [Authorize(Roles = "Admin,Translator")]
        public IActionResult CreateChapterApi([FromBody] ChapterCreateRequest req)
        {
            if (req.NovelId == 0 || string.IsNullOrWhiteSpace(req.Number) || string.IsNullOrWhiteSpace(req.Title))
                return BadRequest(new { error = "NovelId, number и title обязательны" });

            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                return Unauthorized();
            }

            var novelContext = _mySqlService.GetNovel(req.NovelId); // Renamed 'novel' to 'novelContext'
            if (novelContext == null)
            {
                return BadRequest(new { error = "Новелла для добавления главы не найдена" });
            }

            // MODIFIED FOR MODERATION
            if (currentUser.Role == UserRole.Translator && _permissionService.CanSubmitChapterForModeration(currentUser, novelContext)) // Use novelContext
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
                _mySqlService.CreateModerationRequest(moderationRequest); // Corrected
                return Accepted(new { message = "Chapter creation request submitted for moderation." });
            }
            else if (_permissionService.CanAddChapterDirectly(currentUser)) // e.g. Admin
            {
                var chapter = new Chapter
                {
                    NovelId = req.NovelId,
                    Number = req.Number,
                    Title = req.Title,
                    Content = req.Content ?? "", // MySqlService handles not saving this to DB
                    Date = req.Date ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                chapter.CreatorId = currentUser.Id; // Set CreatorId
                _mySqlService.CreateChapter(chapter); // Corrected
                return StatusCode(201, new { message = "Chapter created directly." });
            }
            else
            {
                return Forbid("User not authorized for this action or to bypass moderation.");
            }
        }

        // PUT /api/chapters/{id}
        [HttpPut("api/chapters/{id}")] // Adjusted route
        [Authorize(Roles = "Admin,Translator")]
        public IActionResult UpdateChapterApi(int id, [FromBody] ChapterUpdateRequest req) // Renamed
        {
            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                return Unauthorized();
            }

            var chapter = _mySqlService.GetChapter(id);
            if (chapter == null)
                return NotFound(new { error = "Chapter not found" });

            var novelContext = _mySqlService.GetNovel(chapter.NovelId); // Renamed 'novel' to 'novelContext'
            if (novelContext == null)
            {
                // This case should ideally not happen if data integrity is maintained
                return NotFound(new { error = "Родительская новелла для главы не найдена" });
            }

            // MODIFIED FOR MODERATION
            if (currentUser.Role == UserRole.Translator && chapter.CreatorId == currentUser.Id && _permissionService.CanSubmitChapterForModeration(currentUser, novelContext)) // Use novelContext
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
                _mySqlService.CreateModerationRequest(moderationRequest);
                return Accepted(new { message = "Chapter update request submitted for moderation." });
            }
            else if (_permissionService.CanEditChapter(currentUser, chapter, novelContext)) // Use novelContext, e.g. Admin
            {
                chapter.Number = req.Number ?? chapter.Number;
                chapter.Title = req.Title ?? chapter.Title;
                chapter.Content = req.Content ?? chapter.Content; // MySqlService handles not saving this to DB
                _mySqlService.UpdateChapter(chapter); // Corrected
                return Ok(new { message = "Chapter updated directly." });
            }
            else
            {
                return Forbid("User not authorized for this action or to bypass moderation.");
            }
        }

        // DELETE /api/chapters/{id}
        [HttpDelete("api/chapters/{id}")] // Adjusted route
        [Authorize(Roles = "Admin,Translator")]
        public IActionResult DeleteChapterApi(int id) // Renamed
        {
            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                return Unauthorized();
            }

            var chapter = _mySqlService.GetChapter(id);
            if (chapter == null)
                return NotFound(new { error = "Chapter not found" });

            var novelContext = _mySqlService.GetNovel(chapter.NovelId); // Renamed 'novel' to 'novelContext'
            if (novelContext == null)
            {
                // This case should ideally not happen if data integrity is maintained
                return NotFound(new { error = "Родительская новелла для главы не найдена" });
            }

            // MODIFIED FOR MODERATION
            if (currentUser.Role == UserRole.Translator && chapter.CreatorId == currentUser.Id && _permissionService.CanSubmitChapterForModeration(currentUser, novelContext)) // Use novelContext
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
                _mySqlService.CreateModerationRequest(moderationRequest);
                return Accepted(new { message = "Chapter deletion request submitted for moderation." });
            }
            else if (_permissionService.CanDeleteChapter(currentUser, chapter, novelContext)) // Use novelContext, e.g. Admin
            {
                _mySqlService.DeleteChapter(id);
                return Ok(new { message = "Chapter deleted directly." });
            }
            else
            {
                return Forbid("User not authorized for this action or to bypass moderation.");
            }
        }

        [HttpPost("api/chapters/{chapterId}/upload-image")] // Adjusted route
        [Authorize(Roles = "Admin,Translator")]
        public async Task<IActionResult> UploadImageApi(int chapterId, IFormFile image) // Renamed
        {
            // Permission check added
            var chapterForUpload = _mySqlService.GetChapter(chapterId);
            if (chapterForUpload == null) return NotFound("Chapter not found for image upload.");
            var currentUserForUpload = GetCurrentUser();
            var novelContextForUpload = _mySqlService.GetNovel(chapterForUpload.NovelId); // Renamed 'novelForUpload' to 'novelContextForUpload'
            if (novelContextForUpload == null) return NotFound("Novel for chapter not found.");

            if (!_permissionService.CanEditChapter(currentUserForUpload, chapterForUpload, novelContextForUpload) &&  // Use novelContextForUpload
               !(currentUserForUpload.Role == UserRole.Translator && chapterForUpload.CreatorId == currentUserForUpload.Id))
            {
                return Forbid("Not allowed to upload images to this chapter.");
            }

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

    // ViewModels for Chapter MVC Actions are now expected to be in BulbaLib.Models
}