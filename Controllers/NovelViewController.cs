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
        private readonly MySqlService _mySqlService;
        private readonly PermissionService _permissionService;
        private readonly ICurrentUserService _currentUserService;

        public NovelViewController(MySqlService mySqlService, PermissionService permissionService, ICurrentUserService currentUserService)
        {
            _mySqlService = mySqlService;
            _permissionService = permissionService;
            _currentUserService = currentUserService;
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

        [HttpGet("/novel/{id:int}")]
        public IActionResult Details(int id)
        {
            // This is the existing Details action, ensure it also gets user and permissions
            var novel = _mySqlService.GetNovel(id);
            if (novel == null) return NotFound();

            var currentUser = _currentUserService.GetCurrentUser(); // Use injected service
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

        [Authorize(Roles = "Admin,Author")]
        [HttpGet]
        public IActionResult Create()
        {
            var currentUser = _currentUserService.GetCurrentUser(); // Use injected service
            if (currentUser == null || !(_permissionService.CanAddNovelDirectly(currentUser) || _permissionService.CanSubmitNovelForModeration(currentUser)))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }
            // Pass NovelCreateModel to the view
            return View("~/Views/Novel/Create.cshtml", new NovelCreateModel { Covers = "[]" });
        }

        [Authorize(Roles = "Admin,Author")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(NovelCreateModel model) // Changed to NovelCreateModel
        {
            var currentUser = _currentUserService.GetCurrentUser(); // Use injected service
            if (currentUser == null) return Unauthorized();

            // Check permissions again, although [Authorize] attribute should handle role part
            if (!(_permissionService.CanAddNovelDirectly(currentUser) || _permissionService.CanSubmitNovelForModeration(currentUser)))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            if (ModelState.IsValid)
            {
                var novelToCreate = new Novel
                {
                    Title = model.Title,
                    Description = model.Description,
                    Covers = string.IsNullOrWhiteSpace(model.Covers) ? "[]" : model.Covers,
                    Genres = model.Genres,
                    Tags = model.Tags,
                    Type = model.Type,
                    Format = model.Format,
                    ReleaseYear = model.ReleaseYear,
                    AlternativeTitles = model.AlternativeTitles,
                    RelatedNovelIds = model.RelatedNovelIds,
                    Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    AuthorId = _currentUserService.GetCurrentUserId() // Set AuthorId from current user
                };

                if (!novelToCreate.AuthorId.HasValue)
                {
                    ModelState.AddModelError(string.Empty, "Не удалось определить автора новеллы.");
                    return View("~/Views/Novel/Create.cshtml", model);
                }

                if (_permissionService.CanAddNovelDirectly(currentUser)) // Admin
                {
                    // Admin can set AuthorId, but for this flow, it's the current admin.
                    // If an admin should be able to set a different author, UI and logic would need adjustment.
                    novelToCreate.AuthorId = currentUser.Id;
                    int newNovelId = _mySqlService.CreateNovel(novelToCreate);
                    TempData["SuccessMessage"] = "Новелла успешно добавлена.";
                    return RedirectToAction("Details", "NovelView", new { id = newNovelId });
                }
                else if (_permissionService.CanSubmitNovelForModeration(currentUser)) // Author
                {
                    // AuthorId is already set to current user's ID above
                    var moderationRequest = new ModerationRequest
                    {
                        RequestType = ModerationRequestType.AddNovel,
                        UserId = currentUser.Id, // The user making the request
                        RequestData = JsonSerializer.Serialize(novelToCreate), // Serialize the full Novel object intended for creation
                        Status = ModerationStatus.Pending,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                        // NovelId is null because the novel doesn't exist yet
                    };
                    _mySqlService.CreateModerationRequest(moderationRequest);
                    TempData["SuccessMessage"] = "Запрос на добавление новеллы отправлен на модерацию.";
                    return RedirectToAction("Index", "CatalogView"); // Or user's dashboard
                }
            }
            // If model state is invalid, return to the form with errors
            return View("~/Views/Novel/Create.cshtml", model);
        }

        [Authorize(Roles = "Admin,Author")]
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var currentUser = _currentUserService.GetCurrentUser(); // Use injected service
            if (currentUser == null) return RedirectToAction("Login", "AuthView");

            Novel novel = _mySqlService.GetNovel(id);
            if (novel == null)
            {
                return NotFound();
            }

            if (!_permissionService.CanEditNovel(currentUser, novel))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            var novelEditModel = new NovelEditModel
            {
                Id = novel.Id,
                Title = novel.Title,
                Description = novel.Description,
                Covers = novel.Covers,
                Genres = novel.Genres,
                Tags = novel.Tags,
                Type = novel.Type,
                Format = novel.Format,
                ReleaseYear = novel.ReleaseYear,
                AlternativeTitles = novel.AlternativeTitles,
                RelatedNovelIds = novel.RelatedNovelIds,
                AuthorId = novel.AuthorId
            };
            if (novel.AuthorId.HasValue)
            {
                var authorUser = _mySqlService.GetUser(novel.AuthorId.Value);
                novelEditModel.AuthorLogin = authorUser?.Login;
            }
            return View("~/Views/NovelView/Edit.cshtml", novelEditModel);
        }

        [Authorize(Roles = "Admin,Author")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, NovelEditModel model) // Changed to NovelEditModel
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null) return Unauthorized();

            if (id != model.Id)
            {
                return BadRequest();
            }

            Novel originalNovel = _mySqlService.GetNovel(id);
            if (originalNovel == null)
            {
                return NotFound("Оригинальная новелла не найдена.");
            }

            if (!_permissionService.CanEditNovel(currentUser, originalNovel))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            if (ModelState.IsValid)
            {
                var novelWithChanges = new Novel
                {
                    Id = originalNovel.Id,
                    Title = model.Title,
                    Description = model.Description,
                    Covers = string.IsNullOrWhiteSpace(model.Covers) ? "[]" : model.Covers,
                    Genres = model.Genres,
                    Tags = model.Tags,
                    Type = model.Type,
                    Format = model.Format,
                    ReleaseYear = model.ReleaseYear,
                    AlternativeTitles = model.AlternativeTitles,
                    RelatedNovelIds = model.RelatedNovelIds,
                    AuthorId = originalNovel.AuthorId, // Preserve original AuthorId
                    Date = originalNovel.Date, // Preserve original creation date
                    TranslatorId = originalNovel.TranslatorId // Preserve original, unless editable in NovelEditModel
                };
                // If Admin is allowed to change AuthorId, and NovelEditModel contains a new AuthorId:
                // if (currentUser.Role == "Admin" && model.AuthorId.HasValue && model.AuthorId != originalNovel.AuthorId)
                // {
                //    novelWithChanges.AuthorId = model.AuthorId;
                // }


                if (_permissionService.CanAddNovelDirectly(currentUser)) // Admin can edit directly
                {
                    // Update originalNovel with changes
                    originalNovel.Title = novelWithChanges.Title;
                    originalNovel.Description = novelWithChanges.Description;
                    originalNovel.Covers = novelWithChanges.Covers;
                    originalNovel.Genres = novelWithChanges.Genres;
                    originalNovel.Tags = novelWithChanges.Tags;
                    originalNovel.Type = novelWithChanges.Type;
                    originalNovel.Format = novelWithChanges.Format;
                    originalNovel.ReleaseYear = novelWithChanges.ReleaseYear;
                    originalNovel.AlternativeTitles = novelWithChanges.AlternativeTitles;
                    originalNovel.RelatedNovelIds = novelWithChanges.RelatedNovelIds;
                    // originalNovel.AuthorId = novelWithChanges.AuthorId; // If admin can change author
                    // originalNovel.TranslatorId = novelWithChanges.TranslatorId; // If admin can change translator

                    _mySqlService.UpdateNovel(originalNovel);
                    TempData["SuccessMessage"] = "Новелла успешно обновлена.";
                    return RedirectToAction("Details", "NovelView", new { id = originalNovel.Id });
                }
                else if (currentUser.Role == UserRole.Author && originalNovel.AuthorId == currentUser.Id) // Author (owner) submits for moderation
                {
                    var moderationRequest = new ModerationRequest
                    {
                        RequestType = ModerationRequestType.EditNovel,
                        UserId = currentUser.Id,
                        NovelId = originalNovel.Id,
                        RequestData = JsonSerializer.Serialize(novelWithChanges),
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
                    return RedirectToAction("AccessDenied", "AuthView");
                }
            }
            // If model state is invalid, repopulate non-mapped fields for display
            model.AuthorLogin = _mySqlService.GetUser(model.AuthorId ?? 0)?.Login;
            return View("~/Views/NovelView/Edit.cshtml", model);
        }

        [Authorize(Roles = "Admin,Author")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            var currentUser = _currentUserService.GetCurrentUser(); // Use injected service
            if (currentUser == null) return Unauthorized();

            Novel novel = _mySqlService.GetNovel(id);
            if (novel == null)
            {
                return NotFound();
            }

            if (!_permissionService.CanDeleteNovel(currentUser, novel))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            if (_permissionService.CanAddNovelDirectly(currentUser)) // Admin can delete directly
            {
                _mySqlService.DeleteNovel(id);
                TempData["SuccessMessage"] = "Новелла успешно удалена.";
                return RedirectToAction("Index", "CatalogView");
            }
            // Author (owner) submits for moderation
            else if (currentUser.Role == UserRole.Author && novel.AuthorId == currentUser.Id)
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