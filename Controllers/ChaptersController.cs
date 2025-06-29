﻿using BulbaLib.Services;
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
            _logger.LogInformation("Entering Create Chapter (POST) method. NovelId: {NovelId}, Chapter Number: {ChapterNumber}, Chapter Title: {ChapterTitle}, DraftChapterId: {DraftChapterId}", model.NovelId, model.Number, model.Title, model.DraftChapterId);

            var currentUser = _currentUserService.GetCurrentUser();
            var novel = _mySqlService.GetNovel(model.NovelId);

            if (novel == null)
            {
                _logger.LogWarning("Novel with Id {NovelId} not found during chapter creation.", model.NovelId);
                TempData["ErrorMessage"] = "Новелла не найдена.";
                return RedirectToAction("Index", "CatalogView");
            }
            model.NovelTitle = novel.Title; // Ensure NovelTitle is set for view model

            bool canProceed = false;
            if (currentUser != null)
            {
                if (currentUser.Role == UserRole.Admin)
                    canProceed = _permissionService.CanAddChapterDirectly(currentUser);
                else if (currentUser.Role == UserRole.Translator)
                    canProceed = _permissionService.CanSubmitChapterForModeration(currentUser, novel);
            }

            if (!canProceed)
            {
                _logger.LogWarning("User {UserId} (Role: {UserRole}) cannot proceed with chapter creation for NovelId {NovelId}.", currentUser?.Id, currentUser?.Role, model.NovelId);
                TempData["ErrorMessage"] = "У вас нет прав для выполнения этого действия.";
                return Redirect($"/novel/{model.NovelId}");
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("ModelState is invalid for Create Chapter. Errors: {ModelStateErrors}, NovelId: {NovelId}", JsonSerializer.Serialize(ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)), model.NovelId);
                return View("~/Views/Chapter/Create.cshtml", model);
            }

            Chapter chapterToProcess;
            bool isUpdatingDraft = false;

            if (model.DraftChapterId.HasValue)
            {
                _logger.LogInformation("Processing as an update to draft chapter Id: {DraftChapterId}", model.DraftChapterId.Value);
                chapterToProcess = await _mySqlService.GetChapterAsync(model.DraftChapterId.Value);

                if (chapterToProcess == null)
                {
                    _logger.LogWarning("Draft chapter with Id {DraftChapterId} not found.", model.DraftChapterId.Value);
                    TempData["ErrorMessage"] = "Указанный черновик главы не найден. Пожалуйста, попробуйте снова.";
                    return View("~/Views/Chapter/Create.cshtml", model);
                }
                if (chapterToProcess.CreatorId != currentUser.Id || chapterToProcess.NovelId != model.NovelId)
                {
                    _logger.LogWarning("Attempt to update draft chapter {DraftChapterId} by user {UserId} failed due to ownership/novel mismatch.", model.DraftChapterId.Value, currentUser.Id);
                    TempData["ErrorMessage"] = "Ошибка при обновлении черновика. Убедитесь, что вы редактируете свой черновик для правильной новеллы.";
                    return View("~/Views/Chapter/Create.cshtml", model);
                }
                // Обновляем поля черновика данными из формы
                chapterToProcess.Number = model.Number;
                chapterToProcess.Title = model.Title;
                // Date и CreatorId уже установлены при создании черновика, их можно не менять,
                // или обновить Date на DateTimeOffset.UtcNow.ToUnixTimeSeconds() если это требуется.
                // chapterToProcess.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); 
                isUpdatingDraft = true;
            }
            else
            {
                _logger.LogInformation("Processing as a new chapter (no DraftChapterId provided).");
                chapterToProcess = new Chapter
                {
                    NovelId = model.NovelId,
                    Number = model.Number,
                    Title = model.Title,
                    Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    CreatorId = currentUser.Id
                };
            }

            string chapterContent = model.Content ?? string.Empty;
            _logger.LogInformation("Chapter content length: {ContentLength} for NovelId: {NovelId}, Chapter: {ChapterNumber} - {ChapterTitle}", chapterContent.Length, chapterToProcess.NovelId, chapterToProcess.Number, chapterToProcess.Title);

            string filePath = null;
            try
            {
                _logger.LogInformation("Attempting to save chapter content for NovelId: {NovelId}, Chapter: {ChapterNumber} - {ChapterTitle}", chapterToProcess.NovelId, chapterToProcess.Number, chapterToProcess.Title);
                filePath = await _fileService.SaveChapterContentAsync(
                    chapterToProcess.NovelId, chapterToProcess.Number, chapterToProcess.Title, chapterContent);
                _logger.LogInformation("FileService.SaveChapterContentAsync result. FilePath: {FilePath}", filePath);

                if (string.IsNullOrEmpty(filePath))
                {
                    _logger.LogError("FileService.SaveChapterContentAsync returned empty or null filePath for NovelId: {NovelId}, Chapter: {ChapterNumber} - {ChapterTitle}", chapterToProcess.NovelId, chapterToProcess.Number, chapterToProcess.Title);
                    ModelState.AddModelError(string.Empty, "Ошибка сохранения содержимого главы.");
                    return View("~/Views/Chapter/Create.cshtml", model);
                }
                chapterToProcess.ContentFilePath = filePath;

                if (isUpdatingDraft)
                {
                    await _mySqlService.UpdateChapterAsync(chapterToProcess);
                    _logger.LogInformation("Successfully updated draft chapter in DB. Chapter Id: {ChapterId}", chapterToProcess.Id);
                }
                else
                {
                    await _mySqlService.CreateChapterAsync(chapterToProcess); // chapterToProcess.Id будет установлен здесь
                    _logger.LogInformation("Successfully created new chapter in DB. Chapter Id: {ChapterId}", chapterToProcess.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred during chapter {ActionType} for NovelId: {NovelId}. FilePath attempted: {FilePath}", isUpdatingDraft ? "update" : "creation", model.NovelId, filePath);
                ModelState.AddModelError(string.Empty, "Произошла внутренняя ошибка при сохранении главы.");
                return View("~/Views/Chapter/Create.cshtml", model);
            }

            // Дальнейшая логика (модерация или прямая публикация)
            if (currentUser.Role == UserRole.Admin)
            {
                _logger.LogInformation("Admin (Id: {AdminId}) {Action} chapter directly. Chapter Id: {ChapterId}", currentUser.Id, isUpdatingDraft ? "updated" : "created", chapterToProcess.Id);
                // Уведомление о новой/обновленной главе
                try
                {
                    // Для обновленного черновика уведомление может быть таким же, как для новой главы
                    _logger.LogInformation("Sending new/updated chapter notifications for NovelId: {NovelId}, ChapterId: {ChapterId}", novel.Id, chapterToProcess.Id);
                    await _notificationService.CreateNotificationForSubscribedUsers(novel.Id, chapterToProcess.Id, novel.Title, chapterToProcess.Number ?? chapterToProcess.Title);
                    _logger.LogInformation("Successfully sent new/updated chapter notifications for NovelId: {NovelId}, ChapterId: {ChapterId}", novel.Id, chapterToProcess.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending new/updated chapter notifications for NovelId: {NovelId}, ChapterId: {ChapterId}", novel.Id, chapterToProcess.Id);
                }

                var translators = _mySqlService.GetTranslatorsForNovel(novel.Id);
                if (!translators.Any(t => t.Id == currentUser.Id))
                {
                    _logger.LogInformation("Adding current admin (Id: {AdminId}) as translator for NovelId: {NovelId}", currentUser.Id, novel.Id);
                    await _mySqlService.AddNovelTranslatorIfNotExistsAsync(novel.Id, currentUser.Id);
                }

                TempData["SuccessMessage"] = $"Глава \"{chapterToProcess.Title}\" успешно {(isUpdatingDraft ? "обновлена" : "добавлена")}.";
                return Redirect($"/chapter/{chapterToProcess.Id}");
            }
            else // UserRole.Translator
            {
                _logger.LogInformation("Translator (Id: {TranslatorId}) submitting chapter {ChapterId} for moderation. NovelId: {NovelId}", currentUser.Id, chapterToProcess.Id, model.NovelId);

                // Для модерации, RequestData должен содержать актуальные данные главы, включая ContentFilePath
                // При обновлении черновика, chapterToProcess уже содержит Id.
                // При создании новой (без черновика), chapterToProcess.Id также будет установлен после CreateChapterAsync.
                // Содержимое (chapterContent) уже сохранено в файл, и chapterToProcess.ContentFilePath указывает на него.
                // В RequestData для модерации можно передать сам объект chapterToProcess, но без самого текста (Content),
                // т.к. он уже в файле. Либо специальный DTO.
                // Сейчас AdminController при одобрении AddChapter ожидает Chapter с Content.
                // При одобрении EditChapter ожидает Chapter (или DTO) с Content.
                // Нужно унифицировать или передавать ContentFilePath и чтобы AdminController читал его.
                // Для простоты, передадим chapterContent в RequestData, как и раньше для новых глав.
                // А для обновленных черновиков, которые становятся запросом на модерацию, это будет "AddChapter" с точки зрения модерации.

                var chapterDataForModeration = new Chapter
                {
                    Id = chapterToProcess.Id, // Важно для отслеживания, если это был черновик
                    NovelId = chapterToProcess.NovelId,
                    Number = chapterToProcess.Number,
                    Title = chapterToProcess.Title,
                    Content = chapterContent, // Передаем сам контент для предпросмотра модератором
                    ContentFilePath = chapterToProcess.ContentFilePath, // И путь к файлу
                    Date = chapterToProcess.Date,
                    CreatorId = chapterToProcess.CreatorId
                };
                var requestDataJson = JsonSerializer.Serialize(chapterDataForModeration);

                var moderationRequest = new ModerationRequest
                {
                    // Если это обновление черновика, это все еще "AddChapter" по сути, но с существующим Id.
                    // Или можно ввести новый RequestType "SubmitDraft", но это усложнит AdminController.
                    // Пока оставляем AddChapter, AdminController должен будет это корректно обработать,
                    // если chapterId уже есть в RequestData.
                    RequestType = ModerationRequestType.AddChapter,
                    UserId = currentUser.Id,
                    NovelId = model.NovelId,
                    ChapterId = chapterToProcess.Id, // Указываем ID главы, даже если это "новый" запрос на добавление
                    RequestData = requestDataJson,
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow, // или chapterToProcess.Date, если это дата создания черновика
                    UpdatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateModerationRequest(moderationRequest);
                _logger.LogInformation("Moderation request for chapter Id {ChapterId} created. RequestId: {ModerationRequestId}", chapterToProcess.Id, moderationRequest.Id);

                TempData["SuccessMessage"] = "Запрос на добавление главы отправлен на модерацию.";
                return Redirect($"/novel/{model.NovelId}");
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

            var chapter = await _mySqlService.GetChapterAsync(id);
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

            var existingChapter = await _mySqlService.GetChapterAsync(model.Id);
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
                return Redirect($"/novel/{model.NovelId}");
            }

            if (!ModelState.IsValid)
            {
                // Ensure NovelTitle is repopulated if returning to view due to invalid ModelState
                model.NovelTitle = novel.Title;
                return View("~/Views/Chapter/Edit.cshtml", model);
            }

            string chapterContent = model.Content ?? string.Empty; // Default to content from textarea, ensure not null

            // Logic for Admins: Direct edit
            if (currentUser.Role == UserRole.Admin)
            {
                _logger.LogInformation("[Admin Edit POST] NovelId: {NovelId}, ChapterId: {ChapterId}, Submitted Model Number: '{ModelNumber}', Title: '{ModelTitle}'",
                    existingChapter.NovelId, model.Id, model.Number, model.Title);

                _logger.LogInformation("[Admin Edit POST] Using content from Content textarea. Length: {ContentLength}", chapterContent.Length);

                string oldFilePath = existingChapter.ContentFilePath; // Get existing path BEFORE updating chapter details
                _logger.LogInformation("[Admin Edit POST] Old ContentFilePath: '{OldFilePath}'", oldFilePath);

                _logger.LogInformation("[Admin Edit POST] Calling SaveChapterContentAsync with NovelId: {NovelId}, Number: '{ChapterNumber}', Title: '{ChapterTitle}'",
                    existingChapter.NovelId, model.Number, model.Title);
                string newFilePath = await _fileService.SaveChapterContentAsync(existingChapter.NovelId, model.Number, model.Title, chapterContent);

                if (string.IsNullOrEmpty(newFilePath))
                {
                    _logger.LogError("[Admin Edit POST] SaveChapterContentAsync returned null or empty. NovelId: {NovelId}, ChapterId: {ChapterId}",
                        existingChapter.NovelId, model.Id);
                    ModelState.AddModelError(string.Empty, "Ошибка сохранения содержимого главы.");
                    model.NovelTitle = novel.Title;
                    return View("~/Views/Chapter/Edit.cshtml", model);
                }
                _logger.LogInformation("[Admin Edit POST] SaveChapterContentAsync returned New ContentFilePath: '{NewFilePath}'", newFilePath);

                existingChapter.Number = model.Number;
                existingChapter.Title = model.Title;
                existingChapter.ContentFilePath = newFilePath;

                _logger.LogInformation("[Admin Edit POST] Before UpdateChapter DB call - ChapterId: {ChapterId}, New Number: '{NewNumber}', New Title: '{NewTitle}', New ContentFilePath: '{NewContentFilePath}'",
                    existingChapter.Id, existingChapter.Number, existingChapter.Title, existingChapter.ContentFilePath);

                _mySqlService.UpdateChapter(existingChapter);
                _logger.LogInformation("[Admin Edit POST] After UpdateChapter DB call for ChapterId: {ChapterId}", existingChapter.Id);

                if (!string.IsNullOrEmpty(oldFilePath) && oldFilePath != newFilePath)
                {
                    _logger.LogInformation("[Admin Edit POST] FilePath changed. Old: '{OldFilePath}', New: '{NewFilePath}'. Attempting to delete old file.", oldFilePath, newFilePath);
                    await _fileService.DeleteChapterContentAsync(oldFilePath);
                }
                else if (string.IsNullOrEmpty(oldFilePath) && !string.IsNullOrEmpty(newFilePath))
                {
                    _logger.LogInformation("[Admin Edit POST] Old FilePath was empty, new FilePath is '{NewFilePath}'. No old file to delete.", newFilePath);
                }
                else if (!string.IsNullOrEmpty(oldFilePath) && oldFilePath == newFilePath)
                {
                    _logger.LogInformation("[Admin Edit POST] FilePath did not change from '{OldFilePath}'. No old file to delete (it was overwritten).", oldFilePath);
                }
                else // Both old and new are null/empty - should ideally not happen if save was successful
                {
                    _logger.LogInformation("[Admin Edit POST] Both old and new FilePaths are null or empty. No file operations for deletion needed.");
                }

                TempData["SuccessMessage"] = "Глава успешно обновлена.";
                // Redirect to the chapter page for Admin after editing
                return Redirect($"/chapter/{existingChapter.Id}");
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
                    Content = chapterContent, // New proposed content
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
                // Redirect to the novel page for Translator if moderation is required for edit
                return Redirect($"/novel/{model.NovelId}");
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

            var chapterToDelete = await _mySqlService.GetChapterAsync(id);
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

                await _mySqlService.DeleteChapterAsync(id); // Assuming DeleteChapter was made async

                // Refined logic to remove translator if it was their last chapter for this novel
                if (chapterToDelete.CreatorId.HasValue)
                {
                    await _mySqlService.RemoveTranslatorIfLastChapterAsync(novel.Id, chapterToDelete.CreatorId.Value);
                    _logger.LogInformation("Checked and potentially removed user {UserId} from NovelTranslators for Novel {NovelId} if it was their last chapter.", chapterToDelete.CreatorId.Value, novel.Id);
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
        public async Task<IActionResult> GetChapterApi(int id)
        {
            var chapter = await _mySqlService.GetChapterAsync(id);
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
        public async Task<IActionResult> UpdateChapterApi(int id, [FromBody] ChapterUpdateRequest req) // Renamed
        {
            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                return Unauthorized();
            }

            var chapter = await _mySqlService.GetChapterAsync(id);
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
        public async Task<IActionResult> DeleteChapterApi(int id) // Renamed
        {
            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                return Unauthorized();
            }

            var chapter = await _mySqlService.GetChapterAsync(id);
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
            var chapterForUpload = await _mySqlService.GetChapterAsync(chapterId);
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

            var novelId = chapterForUpload.NovelId; // Get NovelId
            var uploadDir = Path.Combine("wwwroot", "uploads", "images", novelId.ToString(), chapterId.ToString());
            Directory.CreateDirectory(uploadDir); // Ensures NovelId/ChapterId subdirectories are created
            var fileName = Guid.NewGuid() + Path.GetExtension(image.FileName);
            var filePath = Path.Combine(uploadDir, fileName);

            using (var stream = System.IO.File.Create(filePath))
            {
                await image.CopyToAsync(stream);
            }
            var url = $"/uploads/images/{novelId}/{chapterId}/{fileName}"; // Update URL
            return Ok(new { url });
        }

        // API endpoint for recent updates
        [HttpGet("api/chapters/recentupdates")]
        [AllowAnonymous]
        public IActionResult GetRecentUpdates([FromQuery] int count = 6)
        {
            if (count <= 0) count = 6;
            if (count > 50) count = 50; // Max limit

            var updates = _mySqlService.GetRecentChapterUpdates(count);
            var result = updates.Select(u => new RecentUpdateViewModel
            {
                ChapterId = u.Id, // Assuming ChapterWithNovelInfo has Id for chapter
                NovelId = u.NovelId,
                ChapterNumber = u.Number, // Assuming ChapterWithNovelInfo has Number for chapter
                ChapterTitle = u.Title,   // Assuming ChapterWithNovelInfo has Title for chapter
                ChapterDate = u.Date,     // Assuming ChapterWithNovelInfo has Date for chapter
                NovelTitle = u.NovelTitle,
                NovelCovers = !string.IsNullOrEmpty(u.NovelCoversJson) ? JsonSerializer.Deserialize<List<string>>(u.NovelCoversJson) ?? new List<string>() : new List<string>()
            }).ToList();
            return Ok(result);
        }

        [HttpPost("api/chapters/create-draft")]
        [Authorize(Roles = "Admin,Translator")]
        [ValidateAntiForgeryToken] // Добавляем для защиты от CSRF, если используется с формами или AJAX с токеном
        public async Task<IActionResult> CreateDraft([FromBody] CreateChapterDraftRequest request)
        {
            _logger.LogInformation("Entering CreateDraft method for NovelId: {NovelId}", request.NovelId);

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null)
            {
                _logger.LogWarning("CreateDraft: Current user is null. Unauthorized.");
                return Unauthorized(new { message = "Пользователь не авторизован." });
            }

            var novel = _mySqlService.GetNovel(request.NovelId);
            if (novel == null)
            {
                _logger.LogWarning("CreateDraft: Novel with Id {NovelId} not found.", request.NovelId);
                return NotFound(new { message = "Новелла не найдена." });
            }

            if (!_permissionService.CanCreateChapter(currentUser, novel))
            {
                _logger.LogWarning("CreateDraft: User {UserId} does not have permission to create a chapter for NovelId {NovelId}.", currentUser.Id, request.NovelId);
                return Forbid("У вас нет прав на создание главы для этой новеллы.");
            }

            var draftChapter = new Chapter
            {
                NovelId = request.NovelId,
                CreatorId = currentUser.Id,
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Title = $"Черновик от {DateTime.UtcNow:yyyy-MM-dd HH:mm}", // Временный заголовок
                Number = "", // Пустой номер
                ContentFilePath = null, // Явно указываем, что это черновик без файла контента
                Content = null // Контент пока пуст
            };

            try
            {
                await _mySqlService.CreateChapterAsync(draftChapter); // Метод CreateChapterAsync обновит draftChapter.Id
                _logger.LogInformation("CreateDraft: Successfully created draft chapter with Id {ChapterId} for NovelId {NovelId}.", draftChapter.Id, request.NovelId);
                return Ok(new { chapterId = draftChapter.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateDraft: Exception occurred while creating draft chapter for NovelId {NovelId}.", request.NovelId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Произошла внутренняя ошибка при создании черновика главы." });
            }
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

    // ViewModel for Recent Updates
    public class RecentUpdateViewModel
    {
        public int ChapterId { get; set; }
        public int NovelId { get; set; }
        public string? ChapterNumber { get; set; }
        public string? ChapterTitle { get; set; }
        public long ChapterDate { get; set; }
        public string NovelTitle { get; set; }
        public List<string> NovelCovers { get; set; }
    }
}