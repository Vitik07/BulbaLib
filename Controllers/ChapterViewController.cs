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
        private readonly MySqlService _mySqlService; // Added
        private readonly PermissionService _permissionService; // Added

        public ChapterViewController(MySqlService mySqlService, PermissionService permissionService) // Added
        {
            _mySqlService = mySqlService;
            _permissionService = permissionService;
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
            return _mySqlService.GetUser(userId);
        }

        [HttpGet("/chapter/{id:int}")]
        public IActionResult Read(int id)
        {
            ViewBag.ChapterId = id;
            // TODO: Add logic to fetch chapter and novel, then check CanReadChapter permissions
            // And also set ViewData for Edit/Delete chapter buttons on this page
            return View("~/Views/Chapter/Chapter.cshtml");
        }

        [Authorize]
        [HttpGet]
        public IActionResult Create(int novelId)
        {
            User currentUser = GetCurrentUser();
            Novel novel = _mySqlService.GetNovel(novelId);
            if (novel == null)
            {
                return NotFound("Новелла не найдена.");
            }

            if (currentUser == null || !(_permissionService.CanAddChapterDirectly(currentUser) || _permissionService.CanSubmitChapterForModeration(currentUser, novel)))
            {
                 return RedirectToAction("AccessDenied", "AuthView");
            }

            var chapter = new Chapter { NovelId = novelId };
            ViewData["NovelTitle"] = novel.Title; // For display in the view
            return View("~/Views/Chapter/Create.cshtml", chapter);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Chapter chapterToCreate)
        {
            User currentUser = GetCurrentUser();
            if (currentUser == null) return Unauthorized();

            Novel novel = _mySqlService.GetNovel(chapterToCreate.NovelId);
            if (novel == null)
            {
                ModelState.AddModelError("NovelId", "Указанная новелла не найдена.");
                return View("~/Views/Chapter/Create.cshtml", chapterToCreate);
            }

            if (!(_permissionService.CanAddChapterDirectly(currentUser) || _permissionService.CanSubmitChapterForModeration(currentUser, novel)))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            // Remove Id from ModelState if it's causing issues with validation for a new entity
            ModelState.Remove("Id");

            if (ModelState.IsValid)
            {
                chapterToCreate.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                if (_permissionService.CanAddChapterDirectly(currentUser)) // Admin
                {
                    _mySqlService.CreateChapter(chapterToCreate);
                    TempData["SuccessMessage"] = "Глава успешно добавлена.";
                }
                else if (_permissionService.CanSubmitChapterForModeration(currentUser, novel)) // Translator
                {
                    var moderationRequest = new ModerationRequest
                    {
                        RequestType = ModerationRequestType.AddChapter,
                        UserId = currentUser.Id,
                        NovelId = novel.Id,
                        ChapterId = null, // Chapter doesn't exist yet
                        RequestData = JsonSerializer.Serialize(chapterToCreate),
                        Status = ModerationStatus.Pending,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _mySqlService.CreateModerationRequest(moderationRequest);
                    TempData["SuccessMessage"] = "Запрос на добавление главы отправлен на модерацию.";
                }
                return RedirectToAction("Details", "NovelView", new { id = chapterToCreate.NovelId });
            }

            ViewData["NovelTitle"] = novel.Title; // Re-populate for display if returning to view
            return View("~/Views/Chapter/Create.cshtml", chapterToCreate);
        }

        [Authorize]
        [HttpGet]
        public IActionResult Edit(int id) // Chapter Id
        {
            User currentUser = GetCurrentUser();
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

            if (!_permissionService.CanEditChapter(currentUser, chapter, novel))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            ViewData["NovelTitle"] = novel.Title;
            return View("~/Views/Chapter/Edit.cshtml", chapter);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, Chapter chapterFromForm) // Chapter Id
        {
            User currentUser = GetCurrentUser();
            if (currentUser == null) return Unauthorized();

            if (id != chapterFromForm.Id)
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

            if (!_permissionService.CanEditChapter(currentUser, originalChapter, novel))
            {
                 return RedirectToAction("AccessDenied", "AuthView");
            }

            // Preserve NovelId and update Date
            chapterFromForm.NovelId = originalChapter.NovelId;
            chapterFromForm.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();


            if (ModelState.IsValid)
            {
                if (_permissionService.CanAddChapterDirectly(currentUser)) // Admin can edit chapters directly
                {
                    _mySqlService.UpdateChapter(chapterFromForm);
                    TempData["SuccessMessage"] = "Глава успешно обновлена.";
                }
                // Translators (who are part of the novel's translator list) submit for moderation
                else if (_permissionService.CanSubmitChapterForModeration(currentUser, novel))
                {
                    var moderationRequest = new ModerationRequest
                    {
                        RequestType = ModerationRequestType.EditChapter,
                        UserId = currentUser.Id,
                        NovelId = novel.Id,
                        ChapterId = originalChapter.Id,
                        RequestData = JsonSerializer.Serialize(chapterFromForm),
                        Status = ModerationStatus.Pending,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _mySqlService.CreateModerationRequest(moderationRequest);
                    TempData["SuccessMessage"] = "Запрос на редактирование главы отправлен на модерацию.";
                }
                else
                {
                    // Should be caught by CanEditChapter already
                    return RedirectToAction("AccessDenied", "AuthView");
                }
                return RedirectToAction("Details", "NovelView", new { id = novel.Id });
            }

            ViewData["NovelTitle"] = novel.Title; // Re-populate for display
            return View("~/Views/Chapter/Edit.cshtml", chapterFromForm);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id) // Chapter Id
        {
            User currentUser = GetCurrentUser();
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
            else if (_permissionService.CanSubmitChapterForModeration(currentUser, novel))
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