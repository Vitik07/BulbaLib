using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services; // Added
using BulbaLib.Models;   // Added
using System.Security.Claims; // Added
using Microsoft.AspNetCore.Authorization; // Added
using System; // For DateTimeOffset, DateTime
using System.Text.Json; // For JsonSerializer

namespace BulbaLib.Controllers
{
    public class ChapterViewController : Controller
    {
        private readonly MySqlService _mySqlService;
        private readonly PermissionService _permissionService;
        private readonly ICurrentUserService _currentUserService;
        private readonly INotificationService _notificationService; // Added

        public ChapterViewController(
            MySqlService mySqlService,
            PermissionService permissionService,
            ICurrentUserService currentUserService,
            INotificationService notificationService) // Added
        {
            _mySqlService = mySqlService;
            _permissionService = permissionService;
            _currentUserService = currentUserService;
            _notificationService = notificationService; // Added
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
        [HttpGet]
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
        public IActionResult Create(ChapterCreateModel model) // Changed to ChapterCreateModel
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
                var chapterToSave = new Chapter // This is the entity to be saved or put in request
                {
                    NovelId = model.NovelId,
                    Number = model.Number,
                    Title = model.Title,
                    Content = model.Content,
                    Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                if (currentUser.Role == UserRole.Admin && _permissionService.CanAddChapterDirectly(currentUser))
                {
                    chapterToSave.CreatorId = currentUser.Id;
                    _mySqlService.CreateChapter(chapterToSave);
                    TempData["SuccessMessage"] = "Глава успешно добавлена.";

                    // Notify subscribers if Admin adds directly
                    var newChapterAdmin = _mySqlService.GetChaptersByNovel(novel.Id)
                                             .FirstOrDefault(c => c.Title == chapterToSave.Title && c.Number == chapterToSave.Number && c.Content == chapterToSave.Content);
                    if (newChapterAdmin != null)
                    {
                        var subscribers = _mySqlService.GetUserIdsSubscribedToNovel(novel.Id, new List<string> { "reading", "read", "favorites" });
                        var newChapterMessage = $"Новая глава '{newChapterAdmin.Number} - {newChapterAdmin.Title}' добавлена к новелле '{novel.Title}'.";
                        foreach (var subId in subscribers)
                        {
                            // No need to check if subId is current admin, as admin is not typically "subscribing" for own direct actions.
                            _notificationService.CreateNotification(subId, NotificationType.NewChapter, newChapterMessage, newChapterAdmin.Id, RelatedItemType.Chapter);
                        }
                    }
                }
                // Check for Translator role specifically for moderation submission logic
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
                    // Should not happen if initial checks are correct
                    TempData["ErrorMessage"] = "У вас нет прав для выполнения этого действия.";
                    return View("~/Views/Chapter/Create.cshtml", model);
                }
                return RedirectToAction("Details", "NovelView", new { id = model.NovelId });
            }

            return View("~/Views/Chapter/Create.cshtml", model);
        }

        [Authorize(Roles = "Admin,Translator")]
        [HttpGet]
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
        public IActionResult Edit(int id, ChapterEditModel model) // Changed to ChapterEditModel
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
                var chapterWithChanges = new Chapter
                {
                    Id = originalChapter.Id,
                    NovelId = originalChapter.NovelId, // Keep original NovelId
                    Number = model.Number,
                    Title = model.Title,
                    Content = model.Content,
                    Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds() // Update date
                };

                if (currentUser.Role == UserRole.Admin && _permissionService.CanAddChapterDirectly(currentUser))
                {
                    originalChapter.Number = chapterWithChanges.Number;
                    originalChapter.Title = chapterWithChanges.Title;
                    originalChapter.Content = chapterWithChanges.Content;
                    originalChapter.Date = chapterWithChanges.Date;
                    _mySqlService.UpdateChapter(originalChapter);
                    TempData["SuccessMessage"] = "Глава успешно обновлена.";
                }
                else if (currentUser.Role == UserRole.Translator && _permissionService.CanEditChapter(currentUser, originalChapter, novel))
                {
                    var moderationRequest = new ModerationRequest
                    {
                        RequestType = ModerationRequestType.EditChapter,
                        UserId = currentUser.Id,
                        NovelId = novel.Id,
                        ChapterId = originalChapter.Id,
                        RequestData = JsonSerializer.Serialize(chapterWithChanges),
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
                    return View("~/Views/Chapter/Edit.cshtml", model);
                }
                return RedirectToAction("Details", "NovelView", new { id = novel.Id });
            }

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
                _mySqlService.DeleteChapter(id);
                TempData["SuccessMessage"] = "Глава успешно удалена.";
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