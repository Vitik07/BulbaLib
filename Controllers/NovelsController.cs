using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using BulbaLib.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Text.Json;
using System;
using Microsoft.Extensions.Logging;
using BulbaLib.Services; // Replaced BulbaLib.Interfaces
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http; // Added for IFormFile
using System.ComponentModel.DataAnnotations; // Added for DataAnnotations

namespace BulbaLib.Controllers
{
    // [ApiController] // Removed to make it an MVC controller
    // [Route("api/[controller]")] // Removed standard MVC routing will apply
    public class NovelsController : Controller
    {
        private readonly MySqlService _mySqlService;
        private readonly FileService _fileService;
        private readonly PermissionService _permissionService;
        private readonly ICurrentUserService _currentUserService;
        private readonly INotificationService _notificationService;
        private readonly ILogger<NovelsController> _logger;
        private readonly IWebHostEnvironment _env;

        public NovelsController(
            MySqlService mySqlService,
            FileService fileService,
            PermissionService permissionService,
            ICurrentUserService currentUserService,
            INotificationService notificationService,
            ILogger<NovelsController> logger,
            IWebHostEnvironment env)
        {
            _mySqlService = mySqlService;
            _fileService = fileService;
            _permissionService = permissionService;
            _currentUserService = currentUserService;
            _notificationService = notificationService;
            _logger = logger;
            _env = env;
        }

        private User GetCurrentUser()
        {
            return _currentUserService.GetCurrentUser();
        }

        // MVC Actions Start Here
        [Authorize(Roles = "Author,Admin")]
        [HttpGet]
        public IActionResult Create()
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanCreateNovel(currentUser))
            {
                TempData["ErrorMessage"] = "У вас нет прав для создания новеллы.";
                return RedirectToAction("Index", "Home");
            }
            return View("~/Views/Novel/Create.cshtml", new NovelCreateModel());
        }

        [HttpPost]
        [Authorize(Roles = "Author,Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(NovelCreateModel model)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanCreateNovel(currentUser))
            {
                TempData["ErrorMessage"] = "У вас нет прав для создания новеллы.";
                return RedirectToAction("Index", "Home");
            }

            if (!ModelState.IsValid)
            {
                return View("~/Views/Novel/Create.cshtml", model);
            }

            var novel = new Novel
            {
                Title = model.Title,
                Description = model.Description,
                Genres = model.Genres,
                Tags = model.Tags,
                Type = model.Type,
                Format = model.Format,
                ReleaseYear = model.ReleaseYear,
                AlternativeTitles = model.AlternativeTitles,
                RelatedNovelIds = model.RelatedNovelIds, // Make sure this is handled if it's a list/array
                AuthorId = currentUser.Id, // Default to current user, admin might change it later if UI allows
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            var tempCoverPaths = new List<string>();
            if (model.NewCovers != null)
            {
                foreach (var coverFile in model.NewCovers)
                {
                    if (coverFile.Length > 0)
                    {
                        var tempPath = await _fileService.SaveTempNovelCoverAsync(coverFile);
                        if (!string.IsNullOrEmpty(tempPath))
                        {
                            tempCoverPaths.Add(tempPath);
                        }
                        else
                        {
                            ModelState.AddModelError("NewCovers", "Не удалось сохранить одну или несколько обложек.");
                            // Potentially delete already saved temp files if one fails
                            foreach (var savedTempPath in tempCoverPaths) await _fileService.DeleteCoverAsync(savedTempPath); // Use a generic delete for temp files
                            return View("~/Views/Novel/Create.cshtml", model);
                        }
                    }
                }
                novel.Covers = JsonSerializer.Serialize(tempCoverPaths); // Store temp paths for now
            }

            if (currentUser.Role == UserRole.Admin)
            {
                novel.Status = NovelStatus.Approved;
                int newNovelId = _mySqlService.CreateNovel(novel);

                var finalCoverPaths = new List<string>();
                foreach (var tempPath in tempCoverPaths)
                {
                    var finalPath = await _fileService.CommitTempCoverAsync(tempPath, newNovelId);
                    if (!string.IsNullOrEmpty(finalPath))
                    {
                        finalCoverPaths.Add(finalPath);
                    }
                    else
                    {
                        // Log error, potentially try to delete other committed covers or the novel entry
                        _logger.LogError("Failed to commit cover {TempPath} for novel {NewNovelId}", tempPath, newNovelId);
                    }
                }
                novel.Id = newNovelId; // Set ID for update
                novel.Covers = JsonSerializer.Serialize(finalCoverPaths);
                _mySqlService.UpdateNovel(novel); // Update with final cover paths

                TempData["SuccessMessage"] = "Новелла успешно создана.";
                return RedirectToAction("Novel", "NovelView", new { id = newNovelId }); // Assuming NovelView controller and Novel action
            }
            else // UserRole.Author
            {
                novel.Status = model.IsDraft ? NovelStatus.Draft : NovelStatus.PendingApproval;

                // If it's a draft, save it directly without moderation request for now
                // OR create a moderation request with status Draft if that's the flow.
                // For PendingApproval, it definitely goes to moderation.
                // The current spec implies AddNovel is always for PendingApproval from Author.
                // If IsDraft is true, AuthorId should be set, and it's saved directly.

                if (novel.Status == NovelStatus.Draft)
                {
                    // For drafts, we commit covers directly as they are not yet in a moderation flow
                    // where admin would do the commit.
                    int newNovelId = _mySqlService.CreateNovel(novel); // Creates with temp paths initially
                    var finalCoverPaths = new List<string>();
                    foreach (var tempPath in tempCoverPaths)
                    {
                        var finalPath = await _fileService.CommitTempCoverAsync(tempPath, newNovelId);
                        if (!string.IsNullOrEmpty(finalPath))
                        {
                            finalCoverPaths.Add(finalPath);
                        }
                        else { /* Log error */ }
                    }
                    novel.Id = newNovelId;
                    novel.Covers = JsonSerializer.Serialize(finalCoverPaths);
                    _mySqlService.UpdateNovel(novel);

                    TempData["SuccessMessage"] = "Черновик новеллы успешно сохранен.";
                    return RedirectToAction("Index", "Profile"); // Or a page showing author's drafts
                }
                else // PendingApproval
                {
                    // Novel object (with temp paths in novel.Covers) is serialized for moderation
                    var requestDataJson = JsonSerializer.Serialize(novel);

                    var moderationRequest = new ModerationRequest
                    {
                        RequestType = ModerationRequestType.AddNovel,
                        UserId = currentUser.Id,
                        RequestData = requestDataJson,
                        Status = ModerationStatus.Pending,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _mySqlService.CreateModerationRequest(moderationRequest);

                    // Notify admins (optional, as per spec)
                    // _notificationService.NotifyAdmins("New novel for moderation", $"User {currentUser.Login} submitted a new novel '{novel.Title}' for moderation.");

                    TempData["SuccessMessage"] = "Ваш запрос на добавление новеллы отправлен на модерацию.";
                    return RedirectToAction("Index", "CatalogView"); // Assuming CatalogView is the main catalog
                }
            }
        }

        [Authorize(Roles = "Author,Admin")]
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "Пожалуйста, войдите в систему.";
                return RedirectToAction("Login", "AuthView"); // Assuming AuthView for login
            }

            var novel = _mySqlService.GetNovel(id);
            if (novel == null)
            {
                return NotFound("Новелла не найдена.");
            }

            if (!_permissionService.CanEditNovel(currentUser, novel))
            {
                TempData["ErrorMessage"] = "У вас нет прав для редактирования этой новеллы.";
                return RedirectToAction("Index", "Home"); // Or back to novel page
            }

            var editModel = new NovelEditModel
            {
                Id = novel.Id,
                Title = novel.Title,
                Description = novel.Description,
                Genres = novel.Genres,
                Tags = novel.Tags,
                Type = novel.Type,
                Format = novel.Format,
                ReleaseYear = novel.ReleaseYear,
                AlternativeTitles = novel.AlternativeTitles,
                RelatedNovelIds = novel.RelatedNovelIds,
                ExistingCoverPaths = novel.CoversList ?? new List<string>(),
                // IsDraft could be set if we allow editing a Draft into a non-draft or vice-versa
                // For now, IsDraft is mainly for the Create action's initial state.
                // If novel.Status == NovelStatus.Draft, IsDraft could be true here.
                IsDraft = (novel.Status == NovelStatus.Draft && novel.AuthorId == currentUser.Id)
            };

            return View("~/Views/Novel/Edit.cshtml", editModel);
        }

        [HttpPost]
        [Authorize(Roles = "Author,Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(NovelEditModel model)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "Пожалуйста, войдите в систему.";
                return RedirectToAction("Login", "AuthView");
            }

            var existingNovel = _mySqlService.GetNovel(model.Id);
            if (existingNovel == null)
            {
                return NotFound("Новелла не найдена.");
            }

            if (!_permissionService.CanEditNovel(currentUser, existingNovel))
            {
                TempData["ErrorMessage"] = "У вас нет прав для редактирования этой новеллы.";
                // Maybe redirect to novel page instead of Home?
                return RedirectToAction("Novel", "NovelView", new { id = model.Id });
            }

            if (!ModelState.IsValid)
            {
                // Repopulate ExistingCoverPaths if model state is invalid and we return to view
                model.ExistingCoverPaths = existingNovel.CoversList ?? new List<string>();
                return View("~/Views/Novel/Edit.cshtml", model);
            }

            // Prepare the NovelUpdateRequest or directly update existingNovel for Admin
            // For Authors, NovelUpdateRequest is serialized for moderation.
            // For Admins, existingNovel is updated and then saved.

            var currentCoverPaths = existingNovel.CoversList ?? new List<string>();
            var finalCoverPathsForNovel = new List<string>(currentCoverPaths);

            // 1. Delete covers marked for deletion
            if (model.CoversToDelete != null)
            {
                foreach (var coverToDelete in model.CoversToDelete)
                {
                    if (finalCoverPathsForNovel.Contains(coverToDelete))
                    {
                        await _fileService.DeleteCoverAsync(coverToDelete);
                        finalCoverPathsForNovel.Remove(coverToDelete);
                    }
                }
            }

            // 2. Save new covers as temporary files
            var newTempCoverPaths = new List<string>();
            if (model.NewCovers != null)
            {
                foreach (var newCoverFile in model.NewCovers)
                {
                    if (newCoverFile.Length > 0)
                    {
                        var tempPath = await _fileService.SaveTempNovelCoverAsync(newCoverFile);
                        if (!string.IsNullOrEmpty(tempPath))
                        {
                            newTempCoverPaths.Add(tempPath);
                        }
                        else
                        {
                            ModelState.AddModelError("NewCovers", "Не удалось сохранить одну или несколько новых обложек.");
                            model.ExistingCoverPaths = finalCoverPathsForNovel; // Reflect deletions
                            return View("~/Views/Novel/Edit.cshtml", model);
                        }
                    }
                }
            }

            // This will be the list of covers if the edit is applied directly (by Admin)
            // or the list including temporary paths for Author's moderation request.
            var updatedCoverListForRequest = new List<string>(finalCoverPathsForNovel);
            updatedCoverListForRequest.AddRange(newTempCoverPaths);


            if (currentUser.Role == UserRole.Admin)
            {
                existingNovel.Title = model.Title;
                existingNovel.Description = model.Description;
                existingNovel.Genres = model.Genres;
                existingNovel.Tags = model.Tags;
                existingNovel.Type = model.Type;
                existingNovel.Format = model.Format;
                existingNovel.ReleaseYear = model.ReleaseYear;
                existingNovel.AlternativeTitles = model.AlternativeTitles;
                existingNovel.RelatedNovelIds = model.RelatedNovelIds;
                // Admin might change status directly, if UI allows. For now, keep existing.
                // existingNovel.Status = model.IsDraft ? NovelStatus.Draft : existingNovel.Status; // Example if admin can change draft status

                // Commit new temp covers for Admin
                var committedNewPaths = new List<string>();
                foreach (var tempPath in newTempCoverPaths)
                {
                    var finalPath = await _fileService.CommitTempCoverAsync(tempPath, existingNovel.Id);
                    if (!string.IsNullOrEmpty(finalPath))
                    {
                        committedNewPaths.Add(finalPath);
                    }
                    else { /* Log error */ }
                }
                finalCoverPathsForNovel.AddRange(committedNewPaths); // Add newly committed paths
                existingNovel.Covers = JsonSerializer.Serialize(finalCoverPathsForNovel);

                _mySqlService.UpdateNovel(existingNovel);
                TempData["SuccessMessage"] = "Новелла успешно обновлена.";
                return RedirectToAction("Novel", "NovelView", new { id = existingNovel.Id });
            }
            else // UserRole.Author
            {
                // Authors submit changes for moderation.
                // The NovelUpdateRequest should contain all proposed changes including new temp cover paths.
                var novelUpdateRequest = new NovelUpdateRequest // Using the API DTO, but could be a specific ViewModel
                {
                    Title = model.Title,
                    Description = model.Description,
                    Genres = model.Genres,
                    Tags = model.Tags,
                    Type = model.Type,
                    Format = model.Format,
                    ReleaseYear = model.ReleaseYear,
                    AlternativeTitles = model.AlternativeTitles,
                    // RelatedNovelIds = model.RelatedNovelIds, // Assuming this is part of NovelUpdateRequest DTO
                    Covers = updatedCoverListForRequest, // Includes existing kept paths + new temp paths
                    // AuthorId is not part of request as author cannot change authorship
                };
                // Add RelatedNovelIds if it's part of NovelUpdateRequest or handle it
                // For now, assuming it's implicitly part of the model being updated.

                var requestDataJson = JsonSerializer.Serialize(novelUpdateRequest);
                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.EditNovel,
                    UserId = currentUser.Id,
                    NovelId = existingNovel.Id,
                    RequestData = requestDataJson,
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateModerationRequest(moderationRequest);

                TempData["SuccessMessage"] = "Ваш запрос на редактирование новеллы отправлен на модерацию.";
                return RedirectToAction("Novel", "NovelView", new { id = existingNovel.Id });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Author,Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id) // Changed from Delete(NovelEditModel model) to Delete(int id)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "Пожалуйста, войдите в систему.";
                return RedirectToAction("Login", "AuthView");
            }

            var novelToDelete = _mySqlService.GetNovel(id);
            if (novelToDelete == null)
            {
                return NotFound("Новелла не найдена.");
            }

            if (!_permissionService.CanDeleteNovel(currentUser, novelToDelete))
            {
                TempData["ErrorMessage"] = "У вас нет прав для удаления этой новеллы.";
                return RedirectToAction("Novel", "NovelView", new { id = id });
            }

            if (currentUser.Role == UserRole.Admin)
            {
                // Delete covers
                if (novelToDelete.CoversList != null)
                {
                    foreach (var coverPath in novelToDelete.CoversList)
                    {
                        await _fileService.DeleteCoverAsync(coverPath);
                    }
                }
                // Delete novel from DB
                _mySqlService.DeleteNovel(id);
                // Also delete related chapters, translations, etc. This should be handled by DB cascades or explicitly in MySqlService.DeleteNovel

                TempData["SuccessMessage"] = "Новелла успешно удалена.";
                return RedirectToAction("Index", "CatalogView"); // Or wherever appropriate
            }
            else // UserRole.Author
            {
                // Create moderation request for deletion
                var requestData = JsonSerializer.Serialize(new { Title = novelToDelete.Title, Id = novelToDelete.Id });
                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.DeleteNovel,
                    UserId = currentUser.Id,
                    NovelId = novelToDelete.Id,
                    RequestData = requestData, // Include basic info for the moderator
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateModerationRequest(moderationRequest);

                TempData["SuccessMessage"] = "Ваш запрос на удаление новеллы отправлен на модерацию.";
                return RedirectToAction("Novel", "NovelView", new { id = novelToDelete.Id });
            }
        }
        // MVC Actions End Here


        // Existing API methods below
        // GET /api/novels?search=... 
        [HttpGet("api/novels")]
        [AllowAnonymous]
        public IActionResult GetNovels([FromQuery] string search = "")
        {
            var novels = _mySqlService.GetNovels(search);
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
            var novel = _mySqlService.GetNovel(id);
            if (novel == null)
                return NotFound(new { error = "Новелла не найдена" });

            var currentUser = GetCurrentUser();
            // For API, we might not use ViewData directly, but the permission values could be returned in the response
            bool canEdit = currentUser != null && _permissionService.CanEditNovel(currentUser, novel);
            bool canDelete = currentUser != null && _permissionService.CanDeleteNovel(currentUser, novel);

            // Example of adding to ViewBag for server-side rendering (though this is an API controller)

            var chapters = _mySqlService.GetChaptersByNovel(id) ?? new List<Chapter>();
            int chapterCount = chapters.Count();

            HashSet<int> bookmarkedChapters = null;
            int? bookmarkChapterId = null;

            // Если пользователь авторизован, получаем его закладки
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
                var bookmarks = _mySqlService.GetBookmarks(userId);
                if (bookmarks != null && bookmarks.ContainsKey(id.ToString()))
                {
                    bookmarkedChapters = bookmarks[id.ToString()].Select(b => b.ChapterId).ToHashSet();
                    bookmarkChapterId = bookmarks[id.ToString()]
                        .OrderByDescending(b => b.Date)
                        .Select(b => (int?)b.ChapterId)
                        .FirstOrDefault();
                }
            }

            var author = novel.AuthorId.HasValue ? _mySqlService.GetUser(novel.AuthorId.Value) : null;

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
                    RequestType = ModerationRequestType.AddNovel,
                    UserId = currentUser.Id,
                    NovelId = null,
                    RequestData = JsonSerializer.Serialize(novelDataForModeration),
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateModerationRequest(moderationRequest);
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
                _mySqlService.CreateNovel(novel);
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

            var novel = _mySqlService.GetNovel(id);
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
                    RequestType = ModerationRequestType.EditNovel,
                    UserId = currentUser.Id,
                    NovelId = id,
                    RequestData = JsonSerializer.Serialize(req), // req is NovelUpdateRequest
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateModerationRequest(moderationRequest);
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

                _mySqlService.UpdateNovel(novel);
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

            var novel = _mySqlService.GetNovel(id);
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
                    RequestType = ModerationRequestType.DeleteNovel,
                    UserId = currentUser.Id,
                    NovelId = id,
                    RequestData = JsonSerializer.Serialize(new { novel.Title }), // Optional info
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateModerationRequest(moderationRequest);
                return Accepted(new { message = "Novel deletion request submitted for moderation." });
            }
            else if (currentUser.Role == UserRole.Admin) // Assuming Admin can delete directly
            {
                if (!_permissionService.CanDeleteNovel(currentUser, novel)) // Check if admin actually has direct delete permission
                {
                    return Forbid("Admins are not allowed to delete this novel directly based on current permissions.");
                }
                _mySqlService.DeleteNovel(id);
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

            var novelsFromDb = _mySqlService.SearchNovelsByTitle(query, limit);

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
            var novels = _mySqlService.GetNovelsByIds(idList); // This method needs to exist in MySqlService

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

    // ViewModels for MVC Actions
    public class NovelCreateModel
    {
        [Required(ErrorMessage = "Название обязательно")]
        [StringLength(200, MinimumLength = 1, ErrorMessage = "Название должно быть от 1 до 200 символов")]
        public string Title { get; set; }

        [DataType(DataType.MultilineText)]
        public string Description { get; set; }

        public string Genres { get; set; }
        public string Tags { get; set; }

        [StringLength(100)]
        public string Type { get; set; }

        [StringLength(100)]
        public string Format { get; set; }

        [Range(1900, 2100, ErrorMessage = "Год выпуска должен быть между 1900 и 2100")]
        public int? ReleaseYear { get; set; }

        public string AlternativeTitles { get; set; }
        public string RelatedNovelIds { get; set; }

        public List<IFormFile> NewCovers { get; set; } = new List<IFormFile>();

        public bool IsDraft { get; set; }
    }

    public class NovelEditModel : NovelCreateModel
    {
        public int Id { get; set; }
        public List<string> ExistingCoverPaths { get; set; } = new List<string>();
        public List<string> CoversToDelete { get; set; } = new List<string>();
    }
}