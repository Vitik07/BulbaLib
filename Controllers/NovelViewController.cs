using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services; // Added
using BulbaLib.Models;   // Added
using System.Security.Claims; // Added
using Microsoft.AspNetCore.Authorization; // Added
using System; // For DateTimeOffset, DateTime
using System.Text.Json; // For JsonSerializer

namespace BulbaLib.Controllers
{
    public class NovelViewController : Controller
    {
        private readonly MySqlService _mySqlService; // Added
        private readonly PermissionService _permissionService; // Added

        public NovelViewController(MySqlService mySqlService, PermissionService permissionService) // Added
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

        [HttpGet("/novel/{id:int}")]
        public IActionResult Details(int id)
        {
            // This is the existing Details action, ensure it also gets user and permissions
            var novel = _mySqlService.GetNovel(id);
            if (novel == null) return NotFound();

            var currentUser = GetCurrentUser();
            ViewData["CanEditNovel"] = currentUser != null && _permissionService.CanEditNovel(currentUser, novel);
            ViewData["CanDeleteNovel"] = currentUser != null && _permissionService.CanDeleteNovel(currentUser, novel);

            bool canAddChapter = false;
            if (currentUser != null && novel != null)
            {
                canAddChapter = _permissionService.CanAddChapterDirectly(currentUser) || _permissionService.CanSubmitChapterForModeration(currentUser, novel);
            }
            ViewData["CanAddChapter"] = canAddChapter;
            ViewData["NovelId"] = novel.Id; // For "Add Chapter" button form

            List<Chapter> chaptersFromDb = _mySqlService.GetChaptersByNovel(id);
            var chapterViewModels = new List<ChapterViewModel>();
            if (chaptersFromDb != null)
            {
                foreach (var chapter in chaptersFromDb)
                {
                    bool canEditChapter = false;
                    bool canDeleteChapter = false;
                    if (currentUser != null)
                    {
                        canEditChapter = _permissionService.CanEditChapter(currentUser, chapter, novel);
                        canDeleteChapter = _permissionService.CanDeleteChapter(currentUser, chapter, novel);
                    }
                    chapterViewModels.Add(new ChapterViewModel { Chapter = chapter, CanEdit = canEditChapter, CanDelete = canDeleteChapter });
                }
            }
            ViewData["ChapterViewModels"] = chapterViewModels;
            // ViewData["OriginalChapters"] = chaptersFromDb; // Keep if view still uses it, otherwise remove for cleanliness

            // Add other view data as needed, e.g. for chapters, author, etc.

            // ViewBag.NovelId = id; // Redundant as novel.Id is available and passed in ViewData["NovelId"]
            return View("~/Views/Novel/Novel.cshtml", novel); // Pass the novel model to the view
        }

        [Authorize]
        [HttpGet]
        public IActionResult Create()
        {
            User currentUser = GetCurrentUser();
            if (currentUser == null || !(_permissionService.CanAddNovelDirectly(currentUser) || _permissionService.CanSubmitNovelForModeration(currentUser)))
            {
                // return Forbid(); // Or redirect to a generic access denied page
                return RedirectToAction("AccessDenied", "AuthView");
            }
            var model = new Novel { Covers = "[]" }; // Initialize Covers
            return View("~/Views/Novel/Create.cshtml", model);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Novel novelToCreate)
        {
            User currentUser = GetCurrentUser();
            if (currentUser == null) return Unauthorized(); // Should be caught by Authorize

            if (!(_permissionService.CanAddNovelDirectly(currentUser) || _permissionService.CanSubmitNovelForModeration(currentUser)))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            // Remove Id from ModelState if it's causing issues with validation for a new entity
            ModelState.Remove("Id");
            // Manually validate Covers as JSON if needed, or rely on client-side / model validation attributes
            // For simplicity, we assume Covers will be a valid JSON string or handle specific errors if parsing fails.

            if (ModelState.IsValid)
            {
                novelToCreate.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                novelToCreate.Covers = string.IsNullOrWhiteSpace(novelToCreate.Covers) ? "[]" : novelToCreate.Covers;


                if (_permissionService.CanAddNovelDirectly(currentUser)) // Admin
                {
                    novelToCreate.AuthorId = currentUser.Id; // Admin is author, or could be selectable
                    int newNovelId = _mySqlService.CreateNovel(novelToCreate);
                    TempData["SuccessMessage"] = "Новелла успешно добавлена.";
                    return RedirectToAction("Details", "NovelView", new { id = newNovelId });
                }
                else if (_permissionService.CanSubmitNovelForModeration(currentUser)) // Author
                {
                    novelToCreate.AuthorId = currentUser.Id; // Author is the current user
                    var moderationRequest = new ModerationRequest
                    {
                        RequestType = ModerationRequestType.AddNovel,
                        UserId = currentUser.Id,
                        RequestData = JsonSerializer.Serialize(novelToCreate),
                        Status = ModerationStatus.Pending,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _mySqlService.CreateModerationRequest(moderationRequest);
                    TempData["SuccessMessage"] = "Запрос на добавление новеллы отправлен на модерацию.";
                    return RedirectToAction("Index", "CatalogView");
                }
            }
            // If model state is invalid, return to the form with errors
            return View("~/Views/Novel/Create.cshtml", novelToCreate);
        }

        [Authorize]
        [HttpGet]
        public IActionResult Edit(int id)
        {
            User currentUser = GetCurrentUser();
            if (currentUser == null) return RedirectToAction("Login", "AuthView"); // Or a generic access denied

            Novel novel = _mySqlService.GetNovel(id);
            if (novel == null)
            {
                return NotFound();
            }

            if (!_permissionService.CanEditNovel(currentUser, novel))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            return View("~/Views/Novel/Edit.cshtml", novel);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, Novel novelFromForm)
        {
            User currentUser = GetCurrentUser();
            if (currentUser == null) return Unauthorized();

            if (id != novelFromForm.Id)
            {
                return BadRequest();
            }

            Novel originalNovel = _mySqlService.GetNovel(id);
            if (originalNovel == null)
            {
                return NotFound();
            }

            if (!_permissionService.CanEditNovel(currentUser, originalNovel))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            // Preserve original date unless explicitly changed.
            // For properties not in novelFromForm, or to prevent overposting, explicitly map them.
            // Here, we assume novelFromForm has all relevant fields from the edit form.
            novelFromForm.Date = originalNovel.Date; // Keep original creation date

            if (ModelState.IsValid)
            {
                if (_permissionService.CanAddNovelDirectly(currentUser)) // Admin can edit directly
                {
                    originalNovel.Title = novelFromForm.Title;
                    originalNovel.Description = novelFromForm.Description;
                    originalNovel.Covers = string.IsNullOrWhiteSpace(novelFromForm.Covers) ? "[]" : novelFromForm.Covers;
                    originalNovel.Genres = novelFromForm.Genres;
                    originalNovel.Tags = novelFromForm.Tags;
                    originalNovel.Type = novelFromForm.Type;
                    originalNovel.Format = novelFromForm.Format;
                    originalNovel.ReleaseYear = novelFromForm.ReleaseYear;
                    originalNovel.AlternativeTitles = novelFromForm.AlternativeTitles;
                    // Admin might be able to change AuthorId or TranslatorId, but that's not in this form for now
                    // originalNovel.AuthorId = novelFromForm.AuthorId;
                    // originalNovel.TranslatorId = novelFromForm.TranslatorId;

                    _mySqlService.UpdateNovel(originalNovel);
                    TempData["SuccessMessage"] = "Новелла успешно обновлена.";
                    return RedirectToAction("Details", "NovelView", new { id = originalNovel.Id });
                }
                else if (currentUser.Role == "Author" && originalNovel.AuthorId == currentUser.Id) // Author (owner) submits for moderation
                {
                    var moderationRequest = new ModerationRequest
                    {
                        RequestType = ModerationRequestType.EditNovel,
                        UserId = currentUser.Id,
                        NovelId = originalNovel.Id, // Link to the existing novel
                        RequestData = JsonSerializer.Serialize(novelFromForm), // Send proposed changes
                        Status = ModerationStatus.Pending,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _mySqlService.CreateModerationRequest(moderationRequest);
                    TempData["SuccessMessage"] = "Запрос на редактирование новеллы отправлен на модерацию.";
                    return RedirectToAction("Details", "NovelView", new { id = originalNovel.Id });
                }
                else
                {
                    // This case should ideally be caught by CanEditNovel, but as a fallback:
                    return RedirectToAction("AccessDenied", "AuthView");
                }
            }
            // If model state is invalid, return to the form with errors
            return View("~/Views/Novel/Edit.cshtml", novelFromForm);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            User currentUser = GetCurrentUser();
            if (currentUser == null) return Unauthorized();

            Novel novel = _mySqlService.GetNovel(id);
            if (novel == null)
            {
                return NotFound();
            }

            if (!_permissionService.CanDeleteNovel(currentUser, novel))
            {
                return RedirectToAction("AccessDenied", "AuthView"); // Or return Forbid();
            }

            if (_permissionService.CanAddNovelDirectly(currentUser)) // Admin can delete directly
            {
                _mySqlService.DeleteNovel(id);
                TempData["SuccessMessage"] = "Новелла успешно удалена.";
                return RedirectToAction("Index", "CatalogView");
            }
            // Authors (even owners) are assumed to require moderation for deletion based on CanDeleteNovel logic.
            // If CanDeleteNovel was true for an Author, it implies they own it, so they submit a request.
            else if (currentUser.Role == "Author" && novel.AuthorId == currentUser.Id)
            {
                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.DeleteNovel,
                    UserId = currentUser.Id,
                    NovelId = novel.Id, // Link to the existing novel
                    RequestData = JsonSerializer.Serialize(new { novel.Title }), // Store title for context
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateModerationRequest(moderationRequest);
                TempData["SuccessMessage"] = "Запрос на удаление новеллы отправлен на модерацию.";
                return RedirectToAction("Details", "NovelView", new { id = novel.Id }); // Stay on page after request
            }
            else
            {
                // This case implies a non-admin, non-author_owner who somehow passed CanDeleteNovel.
                // This should ideally not be reached if PermissionService.CanDeleteNovel is correctly implemented.
                // CanDeleteNovel for Author should mean they are the owner.
                return Forbid();
            }
        }
    }
}