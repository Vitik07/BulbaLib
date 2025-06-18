using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services; // Added
using BulbaLib.Models;   // Added
using System.Security.Claims; // Added
using Microsoft.AspNetCore.Authorization; // Added
using System; // For DateTimeOffset, DateTime
using System.Text.Json; // For JsonSerializer
using System.IO; // Added

namespace BulbaLib.Controllers
{
    [Route("Chapter")]
    public class ChapterViewController : Controller
    {
        private readonly MySqlService _mySqlService;
        private readonly PermissionService _permissionService;
        private readonly ICurrentUserService _currentUserService;
        private readonly INotificationService _notificationService; // Added
        private readonly FileService _fileService; // Added

        public ChapterViewController(
            MySqlService mySqlService,
            PermissionService permissionService,
            ICurrentUserService currentUserService,
            INotificationService notificationService, // Added
            FileService fileService) // Added
        {
            _mySqlService = mySqlService;
            _permissionService = permissionService;
            _currentUserService = currentUserService;
            _notificationService = notificationService; // Added
            _fileService = fileService; // Added
        }

        // private User GetCurrentUser() // Replaced by ICurrentUserService
        // {
        //     if (!User.Identity.IsAuthenticated)
        //     {
        //         return null;
        //     }
        //     var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        //     if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
        //     {
        //         return null;
        //     }
        //     return _mySqlService.GetUser(userId);
        // }

        [HttpGet("/chapter/{id:int}")]
        public IActionResult Read(int id)
        {
            ViewBag.ChapterId = id;
            // TODO: Add logic to fetch chapter and novel, then check CanReadChapter permissions
            // And also set ViewData for Edit/Delete chapter buttons on this page
            return View("~/Views/Chapter/Chapter.cshtml");
        }

        [Authorize(Roles = "Admin,Translator")]
        [HttpGet("Create")]
        public IActionResult Create(int novelId)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            Novel novel = _mySqlService.GetNovel(novelId);
            if (novel == null)
            {
                return NotFound("Новелла не найдена.");
            }

            // Permission check: Admin can always add. Translator must be assigned to the novel.
            if (currentUser == null ||
                !((currentUser.Role == UserRole.Admin && _permissionService.CanAddChapterDirectly(currentUser)) ||
                   (currentUser.Role == UserRole.Translator && _permissionService.CanSubmitChapterForModeration(currentUser, novel)))
               )
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            ViewData["NovelTitle"] = novel.Title;
            return View("~/Views/Chapter/Create.cshtml", new ChapterCreateModel { NovelId = novelId });
        }

        [Authorize(Roles = "Admin,Translator")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ChapterCreateModel model) // Changed to ChapterCreateModel and async
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null) return Unauthorized();

            Novel novel = _mySqlService.GetNovel(model.NovelId);
            if (novel == null)
            {
                ModelState.AddModelError("NovelId", "Указанная новелла не найдена.");
                ViewData["NovelTitle"] = "Неизвестная новелла"; // Or fetch if possible, but novel is null
                return View("~/Views/Chapter/Create.cshtml", model);
            }
            ViewData["NovelTitle"] = novel.Title;

            // Permission check again for POST
            if (currentUser == null ||
               !((currentUser.Role == UserRole.Admin && _permissionService.CanAddChapterDirectly(currentUser)) ||
                  (currentUser.Role == UserRole.Translator && _permissionService.CanSubmitChapterForModeration(currentUser, novel)))
              )
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            if (ModelState.IsValid)
            {
                string effectiveContent = model.Content;
                if (model.ChapterTextFile != null && model.ChapterTextFile.Length > 0)
                {
                    using (var reader = new StreamReader(model.ChapterTextFile.OpenReadStream()))
                    {
                        effectiveContent = await reader.ReadToEndAsync();
                    }
                }

                string filePath = await _fileService.SaveChapterContentAsync(novel.Id, model.Number, model.Title, effectiveContent);
                if (string.IsNullOrEmpty(filePath))
                {
                    ModelState.AddModelError(string.Empty, "Ошибка сохранения файла содержимого главы.");
                    ViewData["NovelTitle"] = novel.Title; // Ensure NovelTitle is available for the view
                    return View("~/Views/Chapter/Create.cshtml", model);
                }

                var chapterToSave = new Chapter // This is the entity to be saved or put in request
                {
                    NovelId = model.NovelId,
                    Number = model.Number,
                    Title = model.Title,
                    Content = filePath, // Use file path here
                    Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                if (currentUser.Role == UserRole.Admin && _permissionService.CanAddChapterDirectly(currentUser))
                {
                    chapterToSave.CreatorId = currentUser.Id;
                    _mySqlService.CreateChapter(chapterToSave);
                    TempData["SuccessMessage"] = "Глава успешно добавлена.";

                    // Notify subscribers if Admin adds directly
                    // Note: Content comparison for finding the chapter might need adjustment if it now stores a path.
                    // For simplicity, assuming Title and Number are sufficient for this example.
                    var newChapterAdmin = _mySqlService.GetChaptersByNovel(novel.Id)
                                             .FirstOrDefault(c => c.Title == chapterToSave.Title && c.Number == chapterToSave.Number);
                    if (newChapterAdmin != null)
                    {
                        var subscribers = _mySqlService.GetUserIdsSubscribedToNovel(novel.Id, new List<string> { "reading", "read", "favorites" });
                        var newChapterMessage = $"Новая глава '{newChapterAdmin.Number} - {newChapterAdmin.Title}' добавлена к новелле '{novel.Title}'.";
                        foreach (var subId in subscribers)
                        {
                            _notificationService.CreateNotification(subId, NotificationType.NewChapter, newChapterMessage, newChapterAdmin.Id, RelatedItemType.Chapter);
                        }
                    }
                }
                else if (currentUser.Role == UserRole.Translator && _permissionService.CanSubmitChapterForModeration(currentUser, novel))
                {
                    chapterToSave.CreatorId = currentUser.Id; // Set CreatorId before serialization
                    var moderationRequest = new ModerationRequest
                    {
                        RequestType = ModerationRequestType.AddChapter,
                        UserId = currentUser.Id, // Translator's ID
                        NovelId = novel.Id,
                        RequestData = JsonSerializer.Serialize(chapterToSave), // Data of the chapter to be created
                        Status = ModerationStatus.Pending,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _mySqlService.CreateModerationRequest(moderationRequest);
                    TempData["SuccessMessage"] = "Запрос на добавление главы отправлен на модерацию.";
                }
                else
                {
                    TempData["ErrorMessage"] = "У вас нет прав для выполнения этого действия.";
                    return View("~/Views/Chapter/Create.cshtml", model);
                }
                return RedirectToAction("Details", "NovelView", new { id = model.NovelId });
            }
            ViewData["NovelTitle"] = novel.Title; // Ensure NovelTitle is available if ModelState is invalid
            return View("~/Views/Chapter/Create.cshtml", model);
        }

        [Authorize(Roles = "Admin,Translator")]
        [HttpGet("Edit/{id}")]
        public IActionResult Edit(int id) // Chapter Id
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null) return RedirectToAction("Login", "AuthView");

            Chapter chapter = _mySqlService.GetChapter(id);
            if (chapter == null)
            {
                return NotFound("Глава не найдена.");
            }

            Novel novel = _mySqlService.GetNovel(chapter.NovelId);
            if (novel == null)
            {
                return NotFound("Родительская новелла не найдена.");
            }

            // Permission check: Admin or Translator who owns the chapter (via moderation request or future direct link)
            // For now, CanEditChapter handles Admin. Translator ownership needs more logic based on how chapters are linked.
            // Assuming CanEditChapter is sufficient for now for Admins. Translator logic will be refined in POST.
            if (!_permissionService.CanEditChapter(currentUser, chapter, novel))
            {
                // If not Admin, and not a Translator specifically allowed by CanEditChapter (e.g. assigned to novel)
                if (!(currentUser.Role == UserRole.Translator)) // if not a translator at all, deny
                    return RedirectToAction("AccessDenied", "AuthView");
                // Further checks for translator ownership might be needed here or rely on POST.
                // For GET, we can be a bit more lenient and let POST handle stricter specific translator checks if CanEditChapter is too broad for GET.
                // However, the current CanEditChapter checks if translator is assigned to novel.
            }

            ViewData["NovelTitle"] = novel.Title;
            var chapterEditModel = new ChapterEditModel
            {
                Id = chapter.Id,
                NovelId = chapter.NovelId,
                Number = chapter.Number,
                Title = chapter.Title,
                Content = chapter.Content
            };
            return View("~/Views/Chapter/Edit.cshtml", chapterEditModel);
        }

        [Authorize(Roles = "Admin,Translator")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ChapterEditModel model) // Changed to ChapterEditModel and async
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null) return Unauthorized();

            if (id != model.Id)
            {
                return BadRequest();
            }

            Chapter originalChapter = _mySqlService.GetChapter(id);
            if (originalChapter == null)
            {
                return NotFound("Оригинальная глава не найдена.");
            }

            Novel novel = _mySqlService.GetNovel(originalChapter.NovelId);
            if (novel == null)
            {
                return NotFound("Родительская новелла не найдена.");
            }
            ViewData["NovelTitle"] = novel.Title;


            if (!_permissionService.CanEditChapter(currentUser, originalChapter, novel))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            if (ModelState.IsValid)
            {
                string effectiveContent = model.Content;
                if (model.ChapterTextFile != null && model.ChapterTextFile.Length > 0)
                {
                    using (var reader = new StreamReader(model.ChapterTextFile.OpenReadStream()))
                    {
                        effectiveContent = await reader.ReadToEndAsync();
                    }
                }

                // It's good practice to delete the old file if content is changing and it was a file path.
                // However, current logic assumes originalChapter.Content might be actual text or a path.
                // For simplicity in this step, we're not deleting the old file. This could be a future enhancement.

                string newFilePath = await _fileService.SaveChapterContentAsync(novel.Id, model.Number, model.Title, effectiveContent);
                if (string.IsNullOrEmpty(newFilePath))
                {
                    ModelState.AddModelError(string.Empty, "Ошибка сохранения файла содержимого главы.");
                    ViewData["NovelTitle"] = novel.Title; // Ensure NovelTitle for view
                    return View("~/Views/Chapter/Edit.cshtml", model);
                }

                if (currentUser.Role == UserRole.Admin && _permissionService.CanAddChapterDirectly(currentUser))
                {
                    originalChapter.Number = model.Number;
                    originalChapter.Title = model.Title;
                    originalChapter.Content = newFilePath; // Use new file path
                    originalChapter.Date = originalChapter.Date; // Preserve original date
                    _mySqlService.UpdateChapter(originalChapter);
                    TempData["SuccessMessage"] = "Глава успешно обновлена.";
                }
                else if (currentUser.Role == UserRole.Translator && _permissionService.CanEditChapter(currentUser, originalChapter, novel))
                {
                    var chapterWithChangesForModeration = new Chapter
                    {
                        Id = originalChapter.Id,
                        NovelId = originalChapter.NovelId,
                        Number = model.Number,
                        Title = model.Title,
                        Content = newFilePath, // Use new file path
                        Date = originalChapter.Date // Preserve original date
                    };
                    var moderationRequest = new ModerationRequest
                    {
                        RequestType = ModerationRequestType.EditChapter,
                        UserId = currentUser.Id,
                        NovelId = novel.Id,
                        ChapterId = originalChapter.Id,
                        RequestData = JsonSerializer.Serialize(chapterWithChangesForModeration),
                        Status = ModerationStatus.Pending,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _mySqlService.CreateModerationRequest(moderationRequest);
                    TempData["SuccessMessage"] = "Запрос на редактирование главы отправлен на модерацию.";
                }
                else
                {
                    TempData["ErrorMessage"] = "У вас нет прав для выполнения этого действия.";
                    ViewData["NovelTitle"] = novel.Title; // Ensure NovelTitle for view
                    return View("~/Views/Chapter/Edit.cshtml", model);
                }
                return RedirectToAction("Details", "NovelView", new { id = novel.Id });
            }
            ViewData["NovelTitle"] = novel.Title; // Ensure NovelTitle if ModelState is invalid
            return View("~/Views/Chapter/Edit.cshtml", model);
        }

        [Authorize(Roles = "Admin,Translator")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id) // Chapter Id
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null) return Unauthorized();

            Chapter chapter = _mySqlService.GetChapter(id);
            if (chapter == null)
            {
                return NotFound("Глава не найдена.");
            }

            Novel novel = _mySqlService.GetNovel(chapter.NovelId);
            if (novel == null)
            {
                return NotFound("Родительская новелла не найдена.");
            }

            if (!_permissionService.CanDeleteChapter(currentUser, chapter, novel))
            {
                return RedirectToAction("AccessDenied", "AuthView"); // Or return Forbid();
            }

            if (_permissionService.CanAddChapterDirectly(currentUser)) // Admin can delete chapters directly
            {
                int? nullableCreatorId = chapter.CreatorId; // Store before chapter is deleted
                int novelIdForUpdate = chapter.NovelId;    // Store before chapter is deleted

                _mySqlService.DeleteChapter(id);
                TempData["SuccessMessage"] = "Глава успешно удалена.";

                if (nullableCreatorId.HasValue)
                {
                    int creatorId = nullableCreatorId.Value;
                    var chapterCreator = _mySqlService.GetUser(creatorId);
                    if (chapterCreator != null && chapterCreator.Role == UserRole.Translator)
                    {
                        var novelToUpdate = _mySqlService.GetNovel(novelIdForUpdate);
                        if (novelToUpdate != null)
                        {
                            // Check if any OTHER chapters by this user for this novel still exist
                            var remainingChaptersByThisUser = _mySqlService.GetChaptersByNovel(novelIdForUpdate)
                                                                 .Any(c => c.CreatorId == creatorId);

                            if (!remainingChaptersByThisUser && novelToUpdate.TranslatorIds != null && novelToUpdate.TranslatorIds.Contains(creatorId))
                            {
                                novelToUpdate.TranslatorIds.Remove(creatorId);
                                _mySqlService.UpdateNovel(novelToUpdate);
                                // Optional: Log this removal or notify someone
                            }
                        }
                    }
                }
            }
            // Translators (who are part of the novel's translator list and have CanDeleteChapter = true) submit for moderation
            else if (_permissionService.CanDeleteChapter(currentUser, chapter, novel)) // Changed condition here
            {
                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.DeleteChapter,
                    UserId = currentUser.Id,
                    NovelId = novel.Id,
                    ChapterId = chapter.Id,
                    RequestData = JsonSerializer.Serialize(new { chapter.Number, chapter.Title }), // Store some context
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateModerationRequest(moderationRequest);
                TempData["SuccessMessage"] = "Запрос на удаление главы отправлен на модерацию.";
            }
            else
            {
                // This case implies a user for whom CanDeleteChapter was true, 
                // but they are neither an Admin nor a Translator with submission rights for this novel.
                // This should ideally be prevented by the logic in CanDeleteChapter itself.
                return Forbid();
            }

            return RedirectToAction("Details", "NovelView", new { id = novel.Id });
        }
    }
}