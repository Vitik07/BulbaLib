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

        private string SerializeTagsOrGenres(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return JsonSerializer.Serialize(new List<string>()); // Store as empty JSON array
            }
            var list = input.Split(',')
                             .Select(s => s.Trim())
                             .Where(s => !string.IsNullOrWhiteSpace(s))
                             .ToList();
            return JsonSerializer.Serialize(list);
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
            _logger.LogInformation("Entering MVC Create (GET) method.");
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanCreateNovel(currentUser))
            {
                _logger.LogWarning("User (Id: {UserId}) does not have permission to create novel, or is not logged in.", currentUser?.Id);
                TempData["ErrorMessage"] = "У вас нет прав для создания новеллы.";
                return RedirectToAction("Index", "Home");
            }
            _logger.LogInformation("Exiting MVC Create (GET) method, returning view.");
            return View("~/Views/Novel/Create.cshtml", new NovelCreateModel());
        }

        [HttpPost]
        [Authorize(Roles = "Author,Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(NovelCreateModel model)
        {
            _logger.LogInformation("Entering MVC Create (POST) method. Attempting to create novel with Title: {Title}", model.Title);

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
                // Genres = model.Genres, // Will be processed
                // Tags = model.Tags,     // Will be processed
                Type = model.Type,
                Format = model.Format,
                ReleaseYear = model.ReleaseYear,
                AlternativeTitles = model.AlternativeTitles,
                RelatedNovelIds = model.RelatedNovelIds, // Make sure this is handled if it's a list/array
                AuthorId = currentUser.Id, // Default to current user, admin might change it later if UI allows
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            novel.Genres = SerializeTagsOrGenres(model.Genres);
            novel.Tags = SerializeTagsOrGenres(model.Tags);

            var tempCoverPaths = new List<string>();

            // Handle single CoverFile (main cover)
            if (model.CoverFile != null && model.CoverFile.Length > 0)
            {
                var tempPath = await _fileService.SaveTempNovelCoverAsync(model.CoverFile);
                if (!string.IsNullOrEmpty(tempPath))
                {
                    _logger.LogInformation("Saved temporary main cover to: {TempPath}", tempPath);
                    tempCoverPaths.Add(tempPath);
                }
                else
                {
                    _logger.LogWarning("Failed to save temporary main cover for novel: {Title}", model.Title);
                    ModelState.AddModelError("CoverFile", "Не удалось сохранить основную обложку.");
                    return View("~/Views/Novel/Create.cshtml", model);
                }
            }

            // Handle NewCovers (additional/list of covers)
            if (model.NewCovers != null && model.NewCovers.Any())
            {
                foreach (var coverFile_item in model.NewCovers)
                {
                    if (coverFile_item.Length > 0)
                    {
                        var tempPath = await _fileService.SaveTempNovelCoverAsync(coverFile_item);
                        if (!string.IsNullOrEmpty(tempPath))
                        {
                            _logger.LogInformation("Saved temporary additional cover to: {TempPath}", tempPath);
                            tempCoverPaths.Add(tempPath);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to save a temporary additional cover for novel: {Title}", model.Title);
                            // If one additional cover fails, add error and continue to collect other errors if any
                            ModelState.AddModelError("NewCovers", "Не удалось сохранить одну или несколько дополнительных обложек.");
                        }
                    }
                }
                // If any cover failed, and we had already saved some from CoverFile, clean them up.
                if (!ModelState.IsValid && tempCoverPaths.Any())
                {
                    _logger.LogWarning("Model state invalid after attempting to save additional covers. Cleaning up temporary covers for novel: {Title}", model.Title);
                    // Check if the failed files are among those already added to tempCoverPaths to avoid double deletion
                    // This cleanup is tricky; ideally, SaveTempNovelCoverAsync doesn't leave partial state or returns specific errors.
                    // For now, if model state is invalid and *any* temp paths were made, clean all.
                    foreach (var savedTempPath in tempCoverPaths) { await _fileService.DeleteCoverAsync(savedTempPath); }
                    return View("~/Views/Novel/Create.cshtml", model);
                }
            }

            if (!tempCoverPaths.Any())
            {
                // This check is if neither CoverFile nor NewCovers yielded any paths.
                _logger.LogWarning("No covers were uploaded for novel: {Title}", model.Title);
                ModelState.AddModelError("", "Необходимо загрузить хотя бы одну обложку.");
                return View("~/Views/Novel/Create.cshtml", model);
            }
            _logger.LogInformation("Temporary cover paths for novel {Title}: {TempCoverPaths}", model.Title, JsonSerializer.Serialize(tempCoverPaths));
            novel.Covers = JsonSerializer.Serialize(tempCoverPaths); // Store temp paths for now

            // Set status based on IsDraft before Admin/Author specific logic
            novel.Status = model.IsDraft ? NovelStatus.Draft : NovelStatus.PendingApproval;

            if (currentUser.Role == UserRole.Admin)
            {
                // Admin might override the status or have a different flow
                // For now, if an Admin creates, it could be directly approved or respect IsDraft.
                // Let's assume Admin's draft is also a draft. If not draft, then approved.
                if (!model.IsDraft) // If not a draft, Admin approves it.
                {
                    novel.Status = NovelStatus.Approved;
                }
                _logger.LogInformation("Admin creating novel directly. Novel details: {NovelData}", JsonSerializer.Serialize(novel));
                // If model.IsDraft is true, novel.Status is already NovelStatus.Draft from above.
                int newNovelId = _mySqlService.CreateNovel(novel);
                _logger.LogInformation("Novel created with ID: {NewNovelId} by Admin. Committing covers.", newNovelId);

                var finalCoverPaths = new List<string>();
                foreach (var tempPath in tempCoverPaths)
                {
                    var finalPath = await _fileService.CommitTempCoverAsync(tempPath, newNovelId);
                    if (!string.IsNullOrEmpty(finalPath))
                    {
                        _logger.LogInformation("Committed cover for novel {NewNovelId}: {TempPath} -> {FinalPath}", newNovelId, tempPath, finalPath);
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
                _logger.LogInformation("Updating novel {NewNovelId} with final cover paths: {FinalCoverPaths}", newNovelId, novel.Covers);
                _mySqlService.UpdateNovel(novel); // Update with final cover paths

                TempData["SuccessMessage"] = "Новелла успешно создана.";
                return RedirectToAction("Novel", "NovelView", new { id = newNovelId }); // Assuming NovelView controller and Novel action
            }
            else // UserRole.Author
            {
                if (model.IsDraft) // If Author wants to save as draft
                {
                    novel.Status = NovelStatus.Draft;
                    _logger.LogInformation("Author saving novel as draft. Novel details: {NovelData}", JsonSerializer.Serialize(novel));
                    // Create novel in DB with temp paths first to get an ID
                    int newNovelId = _mySqlService.CreateNovel(novel); // novel.Covers has temp paths
                    _logger.LogInformation("Draft novel created with ID: {NewNovelId} by Author. Committing covers.", newNovelId);

                    var finalCoverPaths = new List<string>();
                    if (tempCoverPaths.Any())
                    {
                        foreach (var tempPath in tempCoverPaths)
                        {
                            var finalPath = await _fileService.CommitTempCoverAsync(tempPath, newNovelId);
                            if (!string.IsNullOrEmpty(finalPath))
                            {
                                _logger.LogInformation("Committed cover for draft novel {NewNovelId}: {TempPath} -> {FinalPath}", newNovelId, tempPath, finalPath);
                                finalCoverPaths.Add(finalPath);
                            }
                            else
                            {
                                _logger.LogWarning("Failed to commit cover {TempPath} for new draft novel {NewNovelId}", tempPath, newNovelId);
                                // Decide if this is a critical error. For now, we'll proceed with what we have.
                            }
                        }
                    }
                    novel.Id = newNovelId; // Set ID for update
                    novel.Covers = JsonSerializer.Serialize(finalCoverPaths); // Update with committed paths
                    _logger.LogInformation("Updating draft novel {NewNovelId} with final cover paths: {FinalCoverPaths}", newNovelId, novel.Covers);
                    _mySqlService.UpdateNovel(novel);

                    TempData["SuccessMessage"] = "Черновик новеллы успешно сохранен.";
                    return RedirectToAction("Edit", new { id = newNovelId }); // Redirect to edit the draft
                }
                else // Author wants to submit for approval
                {
                    novel.Status = NovelStatus.PendingApproval; // Ensure status is PendingApproval
                    // novel.Covers already contains JsonSerializer.Serialize(tempCoverPaths) from earlier
                    // The entire novel object with TEMPORARY cover paths is serialized for the moderation request.
                    // Admin will handle committing these paths upon approval.
                    var requestDataJson = JsonSerializer.Serialize(novel); // Serialize the novel object with temp paths

                    var moderationRequest = new ModerationRequest
                    {
                        RequestType = ModerationRequestType.AddNovel,
                        UserId = currentUser.Id,
                        RequestData = requestDataJson, // Contains novel with temp cover paths
                        Status = ModerationStatus.Pending,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        NovelId = null // NovelId is not known until admin approves and creates it.
                                       // The admin processing will create the novel and then can update this request's NovelId.
                    };
                    _logger.LogInformation("Author submitting novel for moderation. Request data: {ModerationRequestData}", JsonSerializer.Serialize(moderationRequest));
                    _mySqlService.CreateModerationRequest(moderationRequest);

                    TempData["SuccessMessage"] = "Запрос на добавление новеллы отправлен на модерацию.";
                    return RedirectToAction("Index", "CatalogView");
                }
            }
        }

        [Authorize(Roles = "Author,Admin")]
        [HttpGet]
        public IActionResult Edit(int id)
        {
            _logger.LogInformation("Entering MVC Edit (GET) method for novel Id: {NovelId}", id);
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null)
            {
                _logger.LogWarning("User not logged in, cannot edit novel Id: {NovelId}", id);
                TempData["ErrorMessage"] = "Пожалуйста, войдите в систему.";
                return RedirectToAction("Login", "AuthView"); // Assuming AuthView for login
            }

            var novel = _mySqlService.GetNovel(id);
            if (novel == null)
            {
                _logger.LogWarning("Novel with Id: {NovelId} not found for editing.", id);
                return NotFound("Новелла не найдена.");
            }

            if (!_permissionService.CanEditNovel(currentUser, novel))
            {
                _logger.LogWarning("User (Id: {UserId}) does not have permission to edit novel Id: {NovelId}", currentUser.Id, id);
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

            // Populate AuthorLogin
            if (novel.AuthorId.HasValue)
            {
                _logger.LogInformation("Populating AuthorLogin for Edit action, NovelId: {NovelId}, AuthorId: {AuthorId}", id, novel.AuthorId.Value);
                editModel.AuthorLogin = _mySqlService.GetUserLogin(novel.AuthorId.Value);
                if (editModel.AuthorLogin == null)
                {
                    _logger.LogWarning("AuthorLogin not found for AuthorId: {AuthorId} while editing NovelId: {NovelId}", novel.AuthorId.Value, id);
                }
            }
            else
            {
                _logger.LogInformation("NovelId: {NovelId} does not have an AuthorId. AuthorLogin will be null.", id);
            }

            // Fetch all genres and tags for dropdowns or similar UI elements
            _logger.LogInformation("Fetching all genres and tags for Edit action, NovelId: {NovelId}", id);
            ViewData["AllGenres"] = _mySqlService.GetAllGenres();
            ViewData["AllTags"] = _mySqlService.GetAllTags();
            _logger.LogInformation("Successfully fetched {GenreCount} genres and {TagCount} tags for NovelId: {NovelId}",
                ((List<string>)ViewData["AllGenres"])?.Count ?? 0,
                ((List<string>)ViewData["AllTags"])?.Count ?? 0,
                id);

            _logger.LogInformation("Exiting MVC Edit (GET) method for novel Id: {NovelId}, returning view with edit model.", id);
            return View("~/Views/Novel/Edit.cshtml", editModel);
        }

        [HttpPost]
        [Authorize(Roles = "Author,Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(NovelEditModel model)
        {
            _logger.LogInformation("Entering MVC Edit (POST) method for novel Id: {NovelId}, Title: {Title}", model.Id, model.Title);
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
            if (model.CoversToDelete != null && model.CoversToDelete.Any())
            {
                _logger.LogInformation("Covers marked for deletion for novel {NovelId}: {CoversToDelete}", model.Id, JsonSerializer.Serialize(model.CoversToDelete));
                foreach (var coverToDelete in model.CoversToDelete)
                {
                    if (finalCoverPathsForNovel.Contains(coverToDelete))
                    {
                        await _fileService.DeleteCoverAsync(coverToDelete);
                        _logger.LogInformation("Deleted cover: {CoverPath} for novel {NovelId}", coverToDelete, model.Id);
                        finalCoverPathsForNovel.Remove(coverToDelete);
                    }
                }
            }

            // 2. Save new covers as temporary files
            var newTempCoverPaths = new List<string>();
            if (model.NewCovers != null && model.NewCovers.Any())
            {
                _logger.LogInformation("New covers being uploaded for novel {NovelId}", model.Id);
                foreach (var newCoverFile in model.NewCovers)
                {
                    if (newCoverFile.Length > 0)
                    {
                        var tempPath = await _fileService.SaveTempNovelCoverAsync(newCoverFile);
                        if (!string.IsNullOrEmpty(tempPath))
                        {
                            _logger.LogInformation("Saved new temporary cover to: {TempPath} for novel {NovelId}", tempPath, model.Id);
                            newTempCoverPaths.Add(tempPath);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to save a new temporary cover for novel {NovelId}", model.Id);
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
            _logger.LogInformation("Updated cover list (including new temp paths if any) for novel {NovelId}: {CoverList}", model.Id, JsonSerializer.Serialize(updatedCoverListForRequest));


            if (currentUser.Role == UserRole.Admin)
            {
                _logger.LogInformation("Admin editing novel Id: {NovelId}. Applying changes directly.", model.Id);
                existingNovel.Title = model.Title;
                existingNovel.Description = model.Description;
                existingNovel.Genres = SerializeTagsOrGenres(model.Genres);
                existingNovel.Tags = SerializeTagsOrGenres(model.Tags);
                existingNovel.Type = model.Type;
                existingNovel.Format = model.Format;
                existingNovel.ReleaseYear = model.ReleaseYear;
                existingNovel.AlternativeTitles = model.AlternativeTitles;
                existingNovel.RelatedNovelIds = model.RelatedNovelIds;

                // Admin can change status using IsDraft
                if (model.IsDraft)
                {
                    existingNovel.Status = NovelStatus.Draft;
                }
                else
                {
                    // If it was a Draft and IsDraft is now false, move to Approved.
                    // If it was already Approved/Pending and IsDraft is false, it remains so (effectively Approved by Admin's edit).
                    existingNovel.Status = NovelStatus.Approved;
                }

                // Commit new temp covers for Admin
                var committedNewPaths = new List<string>();
                if (newTempCoverPaths.Any())
                {
                    _logger.LogInformation("Admin committing new covers for novel {NovelId}", existingNovel.Id);
                    foreach (var tempPath in newTempCoverPaths)
                    {
                        var finalPath = await _fileService.CommitTempCoverAsync(tempPath, existingNovel.Id);
                        if (!string.IsNullOrEmpty(finalPath))
                        {
                            _logger.LogInformation("Committed new cover for novel {NovelId}: {TempPath} -> {FinalPath}", existingNovel.Id, tempPath, finalPath);
                            committedNewPaths.Add(finalPath);
                        }
                        else { _logger.LogError("Failed to commit new cover {TempPath} for novel {NovelId} by Admin", tempPath, existingNovel.Id); }
                    }
                    finalCoverPathsForNovel.AddRange(committedNewPaths); // Add newly committed paths
                }
                existingNovel.Covers = JsonSerializer.Serialize(finalCoverPathsForNovel);
                _logger.LogInformation("Admin updating novel {NovelId}. Final novel data before DB update: {NovelData}", existingNovel.Id, JsonSerializer.Serialize(existingNovel));
                _mySqlService.UpdateNovel(existingNovel);
                TempData["SuccessMessage"] = "Новелла успешно обновлена.";
                return RedirectToAction("Novel", "NovelView", new { id = existingNovel.Id });
            }
            else // UserRole.Author
            {
                // Author is editing their own draft
                if (existingNovel.Status == NovelStatus.Draft && existingNovel.AuthorId == currentUser.Id)
                {
                    existingNovel.Title = model.Title;
                    existingNovel.Description = model.Description;
                    existingNovel.Genres = SerializeTagsOrGenres(model.Genres);
                    existingNovel.Tags = SerializeTagsOrGenres(model.Tags);
                    existingNovel.Type = model.Type;
                    existingNovel.Format = model.Format;
                    existingNovel.ReleaseYear = model.ReleaseYear;
                    existingNovel.AlternativeTitles = model.AlternativeTitles;
                    existingNovel.RelatedNovelIds = model.RelatedNovelIds;
                    // AuthorId does not change

                    _logger.LogInformation("Author editing their own draft novel Id: {NovelId}", existingNovel.Id);
                    // Covers were already handled: `finalCoverPathsForNovel` contains kept old paths.
                    // New covers are in `newTempCoverPaths`. Commit them.
                    if (newTempCoverPaths.Any())
                    {
                        _logger.LogInformation("Author committing new covers for draft novel {NovelId}", existingNovel.Id);
                        foreach (var tempPath in newTempCoverPaths)
                        {
                            var finalPath = await _fileService.CommitTempCoverAsync(tempPath, existingNovel.Id);
                            if (!string.IsNullOrEmpty(finalPath))
                            {
                                _logger.LogInformation("Committed new cover for draft novel {NovelId}: {TempPath} -> {FinalPath}", existingNovel.Id, tempPath, finalPath);
                                finalCoverPathsForNovel.Add(finalPath);
                            }
                            else { _logger.LogWarning("Failed to commit cover {TempPath} for draft novel {NovelId}", tempPath, existingNovel.Id); }
                        }
                    }
                    existingNovel.Covers = JsonSerializer.Serialize(finalCoverPathsForNovel.Distinct().ToList());

                    // Author decides to send draft for moderation or keep as draft
                    if (!model.IsDraft)
                    {
                        existingNovel.Status = NovelStatus.PendingApproval;
                        _logger.LogInformation("Author submitting draft novel {NovelId} for moderation. Final novel data: {NovelData}", existingNovel.Id, JsonSerializer.Serialize(existingNovel));
                        _mySqlService.UpdateNovel(existingNovel); // Save changes with final cover paths

                        // Serialize the *entire updated novel* for the moderation request.
                        // This is treated as an "AddNovel" request type because it's the first time it's being submitted.
                        var requestDataJson = JsonSerializer.Serialize(existingNovel);
                        var moderationRequest = new ModerationRequest
                        {
                            RequestType = ModerationRequestType.AddNovel, // First submission is like adding a new novel
                            UserId = currentUser.Id,
                            NovelId = existingNovel.Id, // Link to existing novel ID
                            RequestData = requestDataJson,
                            Status = ModerationStatus.Pending,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _logger.LogInformation("Moderation request for submitting draft novel {NovelId}: {ModerationRequestData}", existingNovel.Id, JsonSerializer.Serialize(moderationRequest));
                        _mySqlService.CreateModerationRequest(moderationRequest);
                        TempData["SuccessMessage"] = "Черновик отправлен на модерацию.";
                    }
                    else // Author continues to save as draft
                    {
                        existingNovel.Status = NovelStatus.Draft; // Ensure it remains draft
                        _logger.LogInformation("Author saving updated draft novel {NovelId}. Final novel data: {NovelData}", existingNovel.Id, JsonSerializer.Serialize(existingNovel));
                        _mySqlService.UpdateNovel(existingNovel);
                        TempData["SuccessMessage"] = "Черновик успешно обновлен.";
                    }
                    return RedirectToAction("Edit", new { id = existingNovel.Id });
                }
                // Author is editing an already published/pending novel -> new moderation request for edit
                else
                {
                    _logger.LogInformation("Author submitting edit request for already published/pending novel Id: {NovelId}", existingNovel.Id);
                    // `updatedCoverListForRequest` contains kept existing final paths + new temporary paths.
                    // This list is what the admin will see and process.

                    // For Author submitting moderation, process Genres and Tags from the model
                    var processedGenresForModeration = SerializeTagsOrGenres(model.Genres);
                    var processedTagsForModeration = SerializeTagsOrGenres(model.Tags);

                    // Create a temporary object for UpdatedFields that has the processed genres/tags
                    // This ensures the moderator sees the intended JSON structure for genres/tags.
                    var updatedFieldsDataForModeration = new
                    {
                        model.Id, // Keep other fields from NovelEditModel as they are
                        model.Title,
                        model.Description,
                        Genres = processedGenresForModeration, // Use processed string
                        Tags = processedTagsForModeration,     // Use processed string
                        model.Type,
                        model.Format,
                        model.ReleaseYear,
                        model.AlternativeTitles,
                        model.RelatedNovelIds,
                        // Note: Covers are handled by KeptExistingCovers and NewTempCoverPaths, not directly in UpdatedFields here.
                        // If Covers were part of NovelEditModel in a way that they should be in UpdatedFields,
                        // they would need to be handled here as well (e.g., model.CoversList if it existed and was updated).
                    };

                    var editDataForModeration = new
                    {
                        NovelId = existingNovel.Id,
                        UpdatedFields = updatedFieldsDataForModeration, // Use the object with processed genres/tags
                        KeptExistingCovers = finalCoverPathsForNovel.Distinct().ToList(),
                        NewTempCoverPaths = newTempCoverPaths
                    };
                    var requestDataJson = JsonSerializer.Serialize(editDataForModeration);

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
                    _logger.LogInformation("Moderation request for editing novel {NovelId}: {ModerationRequestData}", existingNovel.Id, JsonSerializer.Serialize(moderationRequest));
                    _mySqlService.CreateModerationRequest(moderationRequest);

                    TempData["SuccessMessage"] = "Запрос на редактирование новеллы отправлен на модерацию.";
                    return RedirectToAction("Novel", "NovelView", new { id = existingNovel.Id });
                }
            }
        }

        [HttpPost]
        [Authorize(Roles = "Author,Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id) // Changed from Delete(NovelEditModel model) to Delete(int id)
        {
            _logger.LogInformation("Entering MVC Delete (POST) method for novel Id: {NovelId}", id);
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null)
            {
                _logger.LogWarning("User not logged in, cannot delete novel Id: {NovelId}", id);
                TempData["ErrorMessage"] = "Пожалуйста, войдите в систему.";
                return RedirectToAction("Login", "AuthView");
            }

            var novelToDelete = _mySqlService.GetNovel(id);
            if (novelToDelete == null)
            {
                _logger.LogWarning("Novel with Id: {NovelId} not found for deletion.", id);
                return NotFound("Новелла не найдена.");
            }

            if (!_permissionService.CanDeleteNovel(currentUser, novelToDelete))
            {
                _logger.LogWarning("User (Id: {UserId}) does not have permission to delete novel Id: {NovelId}", currentUser.Id, id);
                TempData["ErrorMessage"] = "У вас нет прав для удаления этой новеллы.";
                return RedirectToAction("Novel", "NovelView", new { id = id });
            }

            if (currentUser.Role == UserRole.Admin)
            {
                _logger.LogInformation("Admin deleting novel Id: {NovelId} directly.", id);
                // Delete covers
                if (novelToDelete.CoversList != null)
                {
                    _logger.LogInformation("Deleting covers for novel Id: {NovelId}. Covers: {Covers}", id, JsonSerializer.Serialize(novelToDelete.CoversList));
                    foreach (var coverPath in novelToDelete.CoversList)
                    {
                        await _fileService.DeleteCoverAsync(coverPath);
                    }
                }
                // Delete novel from DB
                _mySqlService.DeleteNovel(id);
                _logger.LogInformation("Novel Id: {NovelId} deleted from DB by Admin.", id);
                // Also delete related chapters, translations, etc. This should be handled by DB cascades or explicitly in MySqlService.DeleteNovel

                TempData["SuccessMessage"] = "Новелла успешно удалена.";
                _logger.LogInformation("Exiting MVC Delete (POST) method for novel Id: {NovelId}. Novel deleted by Admin.", id);
                return RedirectToAction("Index", "CatalogView"); // Or wherever appropriate
            }
            else // UserRole.Author
            {
                _logger.LogInformation("Author requesting deletion for novel Id: {NovelId}. Creating moderation request.", id);
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
                _logger.LogInformation("Moderation request for deleting novel {NovelId}: {ModerationRequestData}", id, JsonSerializer.Serialize(moderationRequest));
                _mySqlService.CreateModerationRequest(moderationRequest);

                TempData["SuccessMessage"] = "Ваш запрос на удаление новеллы отправлен на модерацию.";
                _logger.LogInformation("Exiting MVC Delete (POST) method for novel Id: {NovelId}. Moderation request created.", id);
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
            _logger.LogInformation("Entering GetNovels API method. Search query: '{SearchQuery}'", search);
            var novels = _mySqlService.GetNovels(search);
            _logger.LogInformation("Found {NovelCount} novels for search query: '{SearchQuery}'", novels.Count(), search);
            var result = novels.Select(n => new {
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
            });
            _logger.LogInformation("Exiting GetNovels API method. Returning {ResultCount} novels.", result.Count());
            return Ok(result);
        }

        // GET /api/novels/{id}
        [HttpGet("api/novels/{id}")]
        [AllowAnonymous] // Explicitly allow anonymous access
        public IActionResult GetNovel(int id)
        {
            _logger.LogInformation("Entering GetNovel API method for id: {Id}", id);

            var novel = _mySqlService.GetNovel(id);
            _logger.LogInformation("Result from _mySqlService.GetNovel({Id}): {NovelFound}", id, novel != null);

            if (novel == null)
            {
                _logger.LogWarning("Novel with id: {Id} not found.", id);
                return NotFound(new { error = "Новелла не найдена" });
            }

            var currentUser = GetCurrentUser();
            // For API, we might not use ViewData directly, but the permission values could be returned in the response
            bool canEdit = currentUser != null && _permissionService.CanEditNovel(currentUser, novel);
            bool canDelete = currentUser != null && _permissionService.CanDeleteNovel(currentUser, novel);

            // Example of adding to ViewBag for server-side rendering (though this is an API controller)

            var chapters = _mySqlService.GetChaptersByNovel(id) ?? new List<Chapter>();
            _logger.LogInformation("Result from _mySqlService.GetChaptersByNovel({Id}): {ChapterCount} chapters found.", id, chapters.Count);
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
            var translators = _mySqlService.GetTranslatorsForNovel(id); // novel.Id is the same as id passed to GetNovel

            var chaptersResult = chapters.Select(ch => new {
                id = ch.Id,
                novelId = ch.NovelId,
                number = ch.Number,
                title = ch.Title,
                content = ch.Content,
                date = ch.Date,
                bookmarked = bookmarkedChapters != null && bookmarkedChapters.Contains(ch.Id)
            }).ToList();

            var responseObject = new
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
                translators = translators.Select(t => new { id = t.Id, login = t.Login }).ToList(),
                alternativeTitles = novel.AlternativeTitles,
                chapters = chaptersResult,
                relatedNovelIds = novel.RelatedNovelIds,
                bookmarkChapterId = bookmarkChapterId,
                date = novel.Date // <<<<<< ДОБАВЛЕНО для фронта!
            };

            _logger.LogInformation("Returning novel data for id: {Id}: {NovelData}", id, JsonSerializer.Serialize(responseObject));
            return Ok(responseObject);
        }

        // POST /api/novels
        [HttpPost("api/novels")]
        [Authorize(Roles = "Admin,Author")] // Require Admin or Author role
        public IActionResult CreateNovel([FromBody] NovelCreateRequest req)
        {
            _logger.LogInformation("Entering CreateNovel API method. Request Title: {Title}", req.Title);
            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                _logger.LogWarning("User not authorized for CreateNovel API. This should have been caught by [Authorize].");
                return Unauthorized(); // Should be caught by [Authorize] but good practice
            }

            // Permission check: CanAddNovelDirectly OR CanSubmitNovelForModeration
            // For an API, we might simplify this. If CanAddNovelDirectly, it's direct.
            // If CanSubmitNovelForModeration, the status of the novel should be 'PendingModeration'.
            // This example assumes direct creation if allowed.
            // MODIFIED FOR MODERATION
            if (currentUser.Role == UserRole.Author)
            {
                _logger.LogInformation("Author (Id: {UserId}) attempting to create novel. Creating moderation request.", currentUser.Id);
                if (!_permissionService.CanSubmitNovelForModeration(currentUser))
                {
                    _logger.LogWarning("Author (Id: {UserId}) does not have permission to submit novel for moderation.", currentUser.Id);
                    return Forbid("Authors are not allowed to submit novels for moderation based on current permissions.");
                }

                _logger.LogInformation("Author (Id: {UserId}) attempting to create novel. Creating moderation request.", currentUser.Id);
                if (!_permissionService.CanSubmitNovelForModeration(currentUser))
                {
                    _logger.LogWarning("Author (Id: {UserId}) does not have permission to submit novel for moderation.", currentUser.Id);
                    return Forbid("Authors are not allowed to submit novels for moderation based on current permissions.");
                }

                var novelDataForModeration = new Novel
                {
                    Title = req.Title,
                    Description = req.Description,
                    CoversList = req.Covers,
                    // Genres = req.Genres, // Will be processed
                    // Tags = req.Tags,     // Will be processed
                    Type = req.Type,
                    Format = req.Format,
                    ReleaseYear = req.ReleaseYear,
                    AuthorId = currentUser.Id, // Author creating the novel is the author
                    AlternativeTitles = req.AlternativeTitles,
                    Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                novelDataForModeration.Genres = SerializeTagsOrGenres(req.Genres);
                novelDataForModeration.Tags = SerializeTagsOrGenres(req.Tags);
                _logger.LogInformation("Novel data for moderation by Author (Id: {UserId}): {NovelData}", currentUser.Id, JsonSerializer.Serialize(novelDataForModeration));

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
                _logger.LogInformation("Moderation request created by Author (Id: {UserId}). Request: {ModerationRequest}", currentUser.Id, JsonSerializer.Serialize(moderationRequest));
                return Accepted(new { message = "Novel creation request submitted for moderation." });
            }
            else if (currentUser.Role == UserRole.Admin) // Assuming Admin can create directly
            {
                _logger.LogInformation("Admin (Id: {UserId}) attempting to create novel directly.", currentUser.Id);
                if (!_permissionService.CanAddNovelDirectly(currentUser)) // Check if admin actually has direct add permission
                {
                    _logger.LogWarning("Admin (Id: {UserId}) does not have permission to add novel directly.", currentUser.Id);
                    return Forbid("Admins are not allowed to add novels directly based on current permissions.");
                }
                _logger.LogInformation("Admin (Id: {UserId}) attempting to create novel directly.", currentUser.Id);
                if (!_permissionService.CanAddNovelDirectly(currentUser)) // Check if admin actually has direct add permission
                {
                    _logger.LogWarning("Admin (Id: {UserId}) does not have permission to add novel directly.", currentUser.Id);
                    return Forbid("Admins are not allowed to add novels directly based on current permissions.");
                }
                var novel = new Novel
                {
                    Title = req.Title,
                    Description = req.Description,
                    CoversList = req.Covers,
                    // Genres = req.Genres, // Will be processed
                    // Tags = req.Tags,     // Will be processed
                    Type = req.Type,
                    Format = req.Format,
                    ReleaseYear = req.ReleaseYear,
                    AuthorId = req.AuthorId ?? currentUser.Id, // Admin can specify AuthorId, defaults to self
                    AlternativeTitles = req.AlternativeTitles,
                    Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                novel.Genres = SerializeTagsOrGenres(req.Genres);
                novel.Tags = SerializeTagsOrGenres(req.Tags);
                _logger.LogInformation("Novel data for direct creation by Admin (Id: {UserId}): {NovelData}", currentUser.Id, JsonSerializer.Serialize(novel));
                _mySqlService.CreateNovel(novel);
                _logger.LogInformation("Novel created directly by Admin (Id: {UserId}). New Novel Id likely {NovelId_placeholder}", currentUser.Id, novel.Id); // Novel.Id might not be populated until after CreateNovel returns it
                return StatusCode(201, new { message = "Novel created directly by Admin." });
            }
            else
            {
                _logger.LogWarning("User role not authorized for CreateNovel API. User Id: {UserId}, Role: {UserRole}", currentUser.Id, currentUser.Role);
                return Forbid("User role not authorized for this action.");
            }
        }

        // PUT /api/novels/{id}
        [HttpPut("api/novels/{id}")]
        [Authorize(Roles = "Admin,Author")] // Require Admin or Author role
        public IActionResult UpdateNovel(int id, [FromBody] NovelUpdateRequest req)
        {
            _logger.LogInformation("Entering UpdateNovel API method for novel Id: {NovelId}. Request data: {RequestData}", id, JsonSerializer.Serialize(req));
            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                _logger.LogWarning("User not authorized for UpdateNovel API (Id: {NovelId}). This should have been caught by [Authorize].", id);
                return Unauthorized();
            }

            var novel = _mySqlService.GetNovel(id);
            if (novel == null)
            {
                _logger.LogWarning("Novel with Id: {NovelId} not found for update.", id);
                return NotFound(new { error = "Novel not found" });
            }

            // MODIFIED FOR MODERATION
            if (currentUser.Role == UserRole.Author)
            {
                _logger.LogInformation("Author (Id: {UserId}) attempting to update novel Id: {NovelId}. Creating moderation request.", currentUser.Id, id);
                if (novel.AuthorId != currentUser.Id)
                {
                    _logger.LogWarning("Author (Id: {UserId}) cannot request update for novel Id: {NovelId} as they are not the author.", currentUser.Id, id);
                    return Forbid("Authors can only request updates for their own novels.");
                }
                if (!_permissionService.CanSubmitNovelForModeration(currentUser))
                {
                    _logger.LogWarning("Author (Id: {UserId}) does not have permission to submit novel updates for moderation (Novel Id: {NovelId}).", currentUser.Id, id);
                    return Forbid("Authors are not allowed to submit novel updates for moderation based on current permissions.");
                }

                // Process Genres and Tags for moderation data
                var processedApiUpdateGenres = SerializeTagsOrGenres(req.Genres);
                var processedApiUpdateTags = SerializeTagsOrGenres(req.Tags);

                // Create a new object for RequestData to ensure it contains processed genres/tags
                // Assuming NovelUpdateRequest has similar fields to NovelCreateRequest.
                // If NovelUpdateRequest is different, this object construction needs to match its structure.
                var apiUpdateDataForModeration = new
                {
                    req.Title,
                    req.Description,
                    req.Covers, // Assuming Covers are List<string> as in NovelCreateRequest
                    Genres = processedApiUpdateGenres,
                    Tags = processedApiUpdateTags,
                    req.Type,
                    req.Format,
                    req.ReleaseYear,
                    req.AuthorId, // Assuming AuthorId can be part of the update request
                    req.AlternativeTitles
                    // Add any other fields present in NovelUpdateRequest
                };

                var requestDataJson = JsonSerializer.Serialize(apiUpdateDataForModeration);
                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.EditNovel,
                    UserId = currentUser.Id,
                    NovelId = id,
                    RequestData = requestDataJson, // Use the processed data
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateModerationRequest(moderationRequest);
                _logger.LogInformation("Moderation request for update created by Author (Id: {UserId}) for novel Id: {NovelId}. Request: {ModerationRequest}", currentUser.Id, id, JsonSerializer.Serialize(moderationRequest));
                return Accepted(new { message = "Novel update request submitted for moderation." });
            }
            else if (currentUser.Role == UserRole.Admin) // Assuming Admin can update directly
            {
                _logger.LogInformation("Admin (Id: {UserId}) attempting to update novel Id: {NovelId} directly.", currentUser.Id, id);
                if (!_permissionService.CanEditNovel(currentUser, novel)) // Check if admin actually has direct edit permission
                {
                    _logger.LogWarning("Admin (Id: {UserId}) does not have permission to edit novel Id: {NovelId} directly.", currentUser.Id, id);
                    return Forbid("Admins are not allowed to edit this novel directly based on current permissions.");
                }
                novel.Title = req.Title ?? novel.Title;
                novel.Description = req.Description ?? novel.Description;
                if (req.Covers != null) novel.CoversList = req.Covers;
                novel.Genres = SerializeTagsOrGenres(req.Genres ?? novel.Genres); // Process if req.Genres is not null, else use existing (already processed)
                novel.Tags = SerializeTagsOrGenres(req.Tags ?? novel.Tags);       // Process if req.Tags is not null, else use existing (already processed)
                novel.Type = req.Type ?? novel.Type;
                novel.Format = req.Format ?? novel.Format;
                novel.ReleaseYear = req.ReleaseYear ?? novel.ReleaseYear;
                // Admin might be allowed to change AuthorId, if req.AuthorId is part of NovelUpdateRequest and handled
                if (req.AuthorId.HasValue) novel.AuthorId = req.AuthorId;
                novel.AlternativeTitles = req.AlternativeTitles ?? novel.AlternativeTitles;

                _logger.LogInformation("Novel data for direct update by Admin (Id: {UserId}) for novel Id: {NovelId}: {NovelData}", currentUser.Id, id, JsonSerializer.Serialize(novel));
                _mySqlService.UpdateNovel(novel);
                _logger.LogInformation("Novel Id: {NovelId} updated directly by Admin (Id: {UserId}).", id, currentUser.Id);
                return Ok(new { message = "Novel updated directly by Admin." });
            }
            else
            {
                _logger.LogWarning("User role not authorized for UpdateNovel API. User Id: {UserId}, Role: {UserRole}, Novel Id: {NovelId}", currentUser.Id, currentUser.Role, id);
                return Forbid("User role not authorized for this action.");
            }
        }

        // DELETE /api/novels/{id}
        [HttpDelete("api/novels/{id}")]
        [Authorize(Roles = "Admin,Author")] // Require Admin or Author role
        public IActionResult DeleteNovel(int id)
        {
            _logger.LogInformation("Entering DeleteNovel API method for novel Id: {NovelId}", id);
            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                _logger.LogWarning("User not authorized for DeleteNovel API (Id: {NovelId}). This should have been caught by [Authorize].", id);
                return Unauthorized();
            }

            var novel = _mySqlService.GetNovel(id);
            if (novel == null)
            {
                _logger.LogWarning("Novel with Id: {NovelId} not found for deletion.", id);
                return NotFound(new { error = "Novel not found" });
            }

            // MODIFIED FOR MODERATION
            if (currentUser.Role == UserRole.Author)
            {
                _logger.LogInformation("Author (Id: {UserId}) attempting to delete novel Id: {NovelId}. Creating moderation request.", currentUser.Id, id);
                if (novel.AuthorId != currentUser.Id)
                {
                    _logger.LogWarning("Author (Id: {UserId}) cannot request deletion for novel Id: {NovelId} as they are not the author.", currentUser.Id, id);
                    return Forbid("Authors can only request deletion for their own novels.");
                }
                if (!_permissionService.CanSubmitNovelForModeration(currentUser))
                {
                    _logger.LogWarning("Author (Id: {UserId}) does not have permission to submit novel deletions for moderation (Novel Id: {NovelId}).", currentUser.Id, id);
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
                _logger.LogInformation("Moderation request for deletion created by Author (Id: {UserId}) for novel Id: {NovelId}. Request: {ModerationRequest}", currentUser.Id, id, JsonSerializer.Serialize(moderationRequest));
                return Accepted(new { message = "Novel deletion request submitted for moderation." });
            }
            else if (currentUser.Role == UserRole.Admin) // Assuming Admin can delete directly
            {
                _logger.LogInformation("Admin (Id: {UserId}) attempting to delete novel Id: {NovelId} directly.", currentUser.Id, id);
                if (!_permissionService.CanDeleteNovel(currentUser, novel)) // Check if admin actually has direct delete permission
                {
                    _logger.LogWarning("Admin (Id: {UserId}) does not have permission to delete novel Id: {NovelId} directly.", currentUser.Id, id);
                    return Forbid("Admins are not allowed to delete this novel directly based on current permissions.");
                }
                _mySqlService.DeleteNovel(id);
                _logger.LogInformation("Novel Id: {NovelId} deleted directly by Admin (Id: {UserId}).", id, currentUser.Id);
                return Ok(new { message = "Novel deleted directly by Admin." });
            }
            else
            {
                _logger.LogWarning("User role not authorized for DeleteNovel API. User Id: {UserId}, Role: {UserRole}, Novel Id: {NovelId}", currentUser.Id, currentUser.Role, id);
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

        [HttpGet("api/novels/search")] // Will be routed as /api/Novels/search
        [AllowAnonymous]    // Assuming search should be public
        public IActionResult SearchNovelsApi([FromQuery] string query, [FromQuery] int limit = 5)
        {
            _logger.LogInformation("Entering SearchNovelsApi. Query: '{Query}', Limit: {Limit}", query, limit);
            if (string.IsNullOrWhiteSpace(query))
            {
                _logger.LogWarning("SearchNovelsApi called with empty query.");
                return BadRequest("Search query cannot be empty.");
            }
            if (limit <= 0) limit = 5; // Default limit if invalid
            if (limit > 20) limit = 20; // Max limit

            var novelsFromDb = _mySqlService.SearchNovelsByTitle(query, limit);
            _logger.LogInformation("SearchNovelsApi found {Count} novels for query: '{Query}'", novelsFromDb.Count(), query);

            var results = novelsFromDb.Select(novel => new
            {
                novel.Id,
                novel.Title,
                FirstCoverUrl = GetFirstCover(novel.Covers)
            }).ToList();
            _logger.LogInformation("Exiting SearchNovelsApi. Returning {ResultCount} results.", results.Count);
            return Ok(results);
        }

        // Add using System.Linq; if not already present
        // Add using BulbaLib.Models; if not already present

        [HttpGet("api/novels/detailsByIds")]
        public IActionResult GetNovelDetailsByIds([FromQuery] string ids)
        {
            _logger.LogInformation("Entering GetNovelDetailsByIds API. Requested IDs: '{Ids}'", ids);
            if (string.IsNullOrWhiteSpace(ids))
            {
                _logger.LogWarning("GetNovelDetailsByIds called with empty IDs string.");
                return BadRequest("IDs cannot be empty.");
            }

            var idList = new List<int>();
            try
            {
                idList = ids.Split(',').Select(int.Parse).ToList();
                _logger.LogInformation("Parsed IDs for GetNovelDetailsByIds: {IdList}", JsonSerializer.Serialize(idList));
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "Invalid ID format in GetNovelDetailsByIds. Input: '{Ids}'", ids);
                return BadRequest("Invalid ID format. IDs should be comma-separated integers.");
            }

            if (!idList.Any())
            {
                _logger.LogInformation("No valid IDs provided to GetNovelDetailsByIds after parsing. Input: '{Ids}'", ids);
                return Ok(new List<object>()); // Return empty list if no valid IDs parsed, though split would likely yield one empty string then fail int.Parse
            }

            // Assuming _mySqlService can fetch multiple novels by IDs.
            // If not, this needs to be implemented in MySqlService.
            // For now, let's assume a method GetNovelsByIds exists or iterate.
            var novels = _mySqlService.GetNovelsByIds(idList); // This method needs to exist in MySqlService
            _logger.LogInformation("GetNovelsByIds from service returned {Count} novels for IDs: {IdList}", novels?.Count() ?? 0, JsonSerializer.Serialize(idList));

            if (novels == null || !novels.Any())
            {
                _logger.LogWarning("No novels found for GetNovelDetailsByIds with IDs: {IdList}", JsonSerializer.Serialize(idList));
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
            _logger.LogInformation("Exiting GetNovelDetailsByIds. Returning {ResultCount} novel details.", result.Count);
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
}