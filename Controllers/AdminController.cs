using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;
using BulbaLib.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic; // For List
using Microsoft.Extensions.Logging; // Added for ILogger
using System.Text.Json;

namespace BulbaLib.Controllers
{
    [Authorize(Roles = "Admin")] // Require Admin role for all actions in this controller
    public class AdminController : Controller
    {
        private readonly MySqlService _mySqlService;
        private readonly PermissionService _permissionService;
        private readonly ICurrentUserService _currentUserService;
        private readonly INotificationService _notificationService;
        private readonly FileService _fileService; // Added
        private readonly ILogger<AdminController> _logger; // Added


        public AdminController(
            MySqlService mySqlService,
            PermissionService permissionService,
            ICurrentUserService currentUserService,
            INotificationService notificationService,
            FileService fileService, // Added
            ILogger<AdminController> logger) // Added
        {
            _mySqlService = mySqlService;
            _permissionService = permissionService;
            _currentUserService = currentUserService;
            _notificationService = notificationService;
            _fileService = fileService; // Added
            _logger = logger; // Added
        }

        // private User GetCurrentUser() // Replaced by _currentUserService
        // {
        //     if (!User.Identity.IsAuthenticated) return null;
        //     var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        //     if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId)) return null;
        //     return _mySqlService.GetUser(userId);
        // }

        // Main Admin Panel Page
        public IActionResult Index()
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanViewAdminPanel(currentUser))
            {
                // For AJAX loaded Index, returning a partial with error or just Forbid might be better.
                // But Index is usually a full page load.
                return RedirectToAction("AccessDenied", "AuthView");
            }
            return View("~/Views/Admin/Index.cshtml");
        }

        // Users Management Tab - now returns PartialView
        public IActionResult UsersPartial() // Renamed
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanManageUsers(currentUser))
            {
                return PartialView("~/Views/Shared/_AccessDeniedPartial.cshtml");
            }

            List<User> users = _mySqlService.GetAllUsers();
            return PartialView("~/Views/Admin/_UsersPartial.cshtml", users);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateUserRole(int userId, string newRole) // Renamed method and parameter
        {
            var currentUser = _currentUserService.GetCurrentUser(); // Not strictly needed if PermissionService doesn't use it for this check
            User targetUser = _mySqlService.GetUser(userId);

            if (targetUser == null) return NotFound(new { message = "User not found." });

            // Optional: Permission check if not all Admins can change all roles, or if there are role hierarchies.
            // if (currentUser == null || !_permissionService.CanChangeUserRole(currentUser, targetUser))
            // {
            //     return Forbid(); // Or return appropriate error status
            // }

            if (Enum.TryParse<UserRole>(newRole, true, out UserRole roleEnum))
            {
                _mySqlService.UpdateUserRole(userId, roleEnum.ToString());
                return Ok(new { message = "User role updated successfully." });
            }
            return BadRequest(new { message = "Invalid role specified." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleUserBlock(int userId)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            User targetUser = _mySqlService.GetUser(userId);

            if (targetUser == null) return NotFound(new { message = "User not found." });

            // Optional: Permission check
            // var currentUser = _currentUserService.GetCurrentUser();
            // if (currentUser == null || !_permissionService.CanBlockUser(currentUser, targetUser))
            // {
            //     return Forbid(); 
            // }

            bool newBlockStatus = !targetUser.IsBlocked;
            _mySqlService.SetUserBlockedStatus(userId, newBlockStatus);
            // Message updated to match subtask, but original was more informative. Keeping the spirit of subtask.
            return Ok(new { message = $"User {(newBlockStatus ? "blocked" : "unblocked")} successfully." });
        }

        // Novel Moderation Requests
        public IActionResult NovelRequestsPartial(int page = 1, int pageSize = 10) // Added pageSize, Renamed
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateNovelRequests(currentUser))
            {
                return PartialView("~/Views/Shared/_AccessDeniedPartial.cshtml");
            }

            //pageSize = 10; // Already a parameter
            var novelRequestTypes = new List<ModerationRequestType> // Updated enum values
                { ModerationRequestType.NovelCreate, ModerationRequestType.NovelUpdate, ModerationRequestType.NovelDelete };

            List<ModerationRequest> rawRequests = _mySqlService.GetPendingModerationRequestsByType(novelRequestTypes, pageSize, (page - 1) * pageSize);

            var viewModels = rawRequests.Select(r => {
                var user = _mySqlService.GetUser(r.UserId);
                string novelTitle = r.NovelId.HasValue ? _mySqlService.GetNovel(r.NovelId.Value)?.Title : "N/A (New Novel)";
                Novel proposedData = null;
                NovelUpdateRequest updateData = null;

                if (r.RequestType == ModerationRequestType.NovelCreate && !string.IsNullOrEmpty(r.RequestData))
                {
                    try
                    {
                        proposedData = JsonSerializer.Deserialize<Novel>(r.RequestData);
                        novelTitle = proposedData?.Title ?? "N/A (New Novel)";
                    }
                    catch (JsonException ex) { _logger.LogError(ex, "Failed to deserialize NovelCreate RequestData for request ID {RequestId}", r.Id); }
                }
                else if (r.RequestType == ModerationRequestType.NovelUpdate && !string.IsNullOrEmpty(r.RequestData))
                {
                    try
                    {
                        updateData = JsonSerializer.Deserialize<NovelUpdateRequest>(r.RequestData);
                        // novelTitle is already fetched from existing novel if NovelId.HasValue
                    }
                    catch (JsonException ex) { _logger.LogError(ex, "Failed to deserialize NovelUpdate RequestData for request ID {RequestId}", r.Id); }
                }
                else if (r.RequestType == ModerationRequestType.NovelDelete && !string.IsNullOrEmpty(r.RequestData))
                {
                    // For delete, RequestData might contain { "Title": "..." }
                    try { novelTitle = JsonDocument.Parse(r.RequestData).RootElement.GetProperty("Title").GetString() ?? novelTitle; }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not parse Title from RequestData for NovelDelete request ID {RequestId}", r.Id); }
                }

                // Using existing ViewModel structure which is flat
                return new NovelModerationRequestViewModel
                {
                    RequestId = r.Id,
                    RequestType = r.RequestType,
                    // RequestTypeDisplay is handled by RequestTypeFriendlyName in VM
                    UserId = r.UserId,
                    RequesterLogin = user?.Login ?? "N/A",
                    CreatedAt = r.CreatedAt,
                    NovelId = r.NovelId,
                    NovelTitle = novelTitle,
                    RequestDataJson = r.RequestData, // Keep raw JSON for details view or debugging
                    ProposedNovelData = proposedData, // Populated for NovelCreate
                    // For NovelUpdate, the controller for details view might need to deserialize NovelUpdateRequest
                    // ExistingNovelData could be populated in details view too.
                    Status = r.Status.ToString()
                };
            }).ToList();

            int totalRequests = _mySqlService.CountPendingModerationRequestsByType(novelRequestTypes);

            ViewData["TotalPages"] = (int)Math.Ceiling((double)totalRequests / pageSize);
            ViewData["CurrentPage"] = page;
            ViewData["PageSize"] = pageSize;
            return PartialView("~/Views/Admin/_NovelRequestsPartial.cshtml", viewModels);
        }

        public IActionResult NovelRequestDetails(int requestId)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateNovelRequests(currentUser))
            {
                // Decide if this should be a full redirect or partial for AJAX context if called from one.
                // For now, assume it's a separate page or modal content, so Redirect is okay.
                return RedirectToAction("AccessDenied", "AuthView");
            }

            ModerationRequest request = _mySqlService.GetModerationRequestById(requestId);
            if (request == null) return NotFound();

            // Prepare a specific ViewModel for this page.
            var viewModel = new NovelModerationRequestViewModel
            {
                RequestId = request.Id,
                RequestType = request.RequestType,
                UserId = request.UserId,
                RequesterLogin = _mySqlService.GetUser(request.UserId)?.Login ?? "N/A",
                CreatedAt = request.CreatedAt,
                NovelId = request.NovelId,
                RequestDataJson = request.RequestData,
                Status = request.Status.ToString(),
                NovelTitle = request.NovelId.HasValue ? _mySqlService.GetNovel(request.NovelId.Value)?.Title : null
            };

            if (request.NovelId.HasValue)
            {
                viewModel.ExistingNovelData = _mySqlService.GetNovel(request.NovelId.Value);
                if (viewModel.NovelTitle == null) viewModel.NovelTitle = viewModel.ExistingNovelData?.Title;
            }

            if (request.RequestType == ModerationRequestType.NovelCreate && !string.IsNullOrEmpty(request.RequestData))
            {
                try
                {
                    viewModel.ProposedNovelData = JsonSerializer.Deserialize<Novel>(request.RequestData);
                    if (viewModel.NovelTitle == null) viewModel.NovelTitle = viewModel.ProposedNovelData?.Title;
                }
                catch (JsonException ex) { _logger.LogError(ex, "Failed to deserialize NovelCreate RequestData for details view, request ID {RequestId}", request.Id); }
            }
            else if (request.RequestType == ModerationRequestType.NovelUpdate && !string.IsNullOrEmpty(request.RequestData))
            {
                try
                {
                    // For NovelUpdate, RequestData contains NovelUpdateRequest. We can deserialize it here for the view if needed.
                    // For simplicity, the view might just show the existing novel and then the proposed changes if we add more fields to ViewModel.
                    // Or, the view could use RequestDataJson to show the raw changes.
                    // For now, loading ProposedNovelData as Novel if it's a full update, or specific DTO if only partial.
                    // Assuming NovelUpdateRequest is a subset of Novel fields for now, or that RequestData for edit is also a full Novel object.
                    // The subtask implies NovelUpdateRequest for processing, but for display, full Novel might be easier if RequestData has it.
                    // Let's assume the ViewModel's ProposedNovelData (type Novel) is sufficient for display for edit, 
                    // and the actual processing will use NovelUpdateRequest.
                    viewModel.ProposedNovelData = JsonSerializer.Deserialize<Novel>(request.RequestData); // Or NovelUpdateRequest
                }
                catch (JsonException ex) { _logger.LogError(ex, "Failed to deserialize NovelUpdate RequestData for details view, request ID {RequestId}", request.Id); }
            }
            else if (request.RequestType == ModerationRequestType.NovelDelete && !string.IsNullOrEmpty(request.RequestData))
            {
                if (viewModel.NovelTitle == null) // If not already set by existing novel
                {
                    try { viewModel.NovelTitle = JsonDocument.Parse(request.RequestData).RootElement.GetProperty("Title").GetString(); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not parse Title from RequestData for NovelDelete details view, request ID {RequestId}", request.Id); }
                }
            }
            if (viewModel.NovelTitle == null) viewModel.NovelTitle = "N/A";


            // Fetch moderator info if available
            if (request.ModeratorId.HasValue)
            {
                ViewData["ModeratorUserLogin"] = _mySqlService.GetUser(request.ModeratorId.Value)?.Login ?? "N/A";
            }

            return View("~/Views/Admin/NovelRequestDetails.cshtml", viewModel); // Pass ViewModel
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessNovelRequest(int requestId, bool approve, string moderationComment)
        {
            var currentAdminUser = _currentUserService.GetCurrentUser(); // Use service
            if (currentAdminUser == null || !_permissionService.CanModerateNovelRequests(currentAdminUser))
            {
                return Json(new { success = false, message = "Недостаточно прав." });
            }

            ModerationRequest request = _mySqlService.GetModerationRequestById(requestId);
            if (request == null) return NotFound(new { message = "Request not found." });
            if (request.Status != ModerationStatus.Pending) return BadRequest(new { message = "Запрос уже обработан." });

            request.ModeratorId = currentAdminUser.Id;
            request.ModerationComment = moderationComment;
            request.UpdatedAt = DateTime.UtcNow;

            Novel novelDataForNotification = null; // For notification title consistency
            Novel existingNovelForNotification = null; // For notification title consistency

            if (approve)
            {
                request.Status = ModerationStatus.Approved;
                try
                {
                    switch (request.RequestType)
                    {
                        case ModerationRequestType.NovelCreate:
                            var novelCreateData = JsonSerializer.Deserialize<Novel>(request.RequestData);
                            if (novelCreateData == null) throw new JsonException("Failed to deserialize NovelCreate data.");

                            novelCreateData.AuthorId = request.UserId; // Ensure AuthorId is from the requester
                            novelCreateData.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            novelCreateData.Status = "approved"; // Set status for the novel itself

                            var novelForDbCreation = new Novel // Temp object for DB, covers handled after ID
                            {
                                Title = novelCreateData.Title,
                                Description = novelCreateData.Description,
                                AuthorId = novelCreateData.AuthorId,
                                Genres = novelCreateData.Genres,
                                Tags = novelCreateData.Tags,
                                Type = novelCreateData.Type,
                                Format = novelCreateData.Format,
                                ReleaseYear = novelCreateData.ReleaseYear,
                                AlternativeTitles = novelCreateData.AlternativeTitles,
                                RelatedNovelIds = novelCreateData.RelatedNovelIds,
                                Date = novelCreateData.Date,
                                Status = novelCreateData.Status,
                                Covers = "[]" // Start with empty covers
                            };
                            int newNovelId = _mySqlService.CreateNovel(novelForDbCreation);
                            novelDataForNotification = novelCreateData; // For notification

                            List<string> tempPathsCreate = new List<string>();
                            if (!string.IsNullOrEmpty(novelCreateData.Covers)) { try { tempPathsCreate = JsonSerializer.Deserialize<List<string>>(novelCreateData.Covers); } catch { } }

                            List<string> finalNovelCoverPathsCreate = new List<string>();
                            if (tempPathsCreate != null)
                            {
                                foreach (var tempPath in tempPathsCreate)
                                {
                                    if (!string.IsNullOrEmpty(tempPath) && tempPath.Contains("/temp_covers/"))
                                    {
                                        string finalPath = await _fileService.CommitTempCoverAsync(tempPath, newNovelId);
                                        if (!string.IsNullOrEmpty(finalPath)) finalNovelCoverPathsCreate.Add(finalPath);
                                    }
                                }
                            }
                            if (finalNovelCoverPathsCreate.Any())
                            {
                                var novelToUpdateCovers = _mySqlService.GetNovel(newNovelId);
                                if (novelToUpdateCovers != null)
                                {
                                    novelToUpdateCovers.Covers = JsonSerializer.Serialize(finalNovelCoverPathsCreate);
                                    _mySqlService.UpdateNovel(novelToUpdateCovers);
                                }
                            }
                            break;

                        case ModerationRequestType.NovelUpdate:
                            var novelUpdateData = JsonSerializer.Deserialize<NovelUpdateRequest>(request.RequestData);
                            if (novelUpdateData == null) throw new JsonException("Failed to deserialize NovelUpdate data.");

                            var novelToUpdate = _mySqlService.GetNovel(request.NovelId.Value);
                            if (novelToUpdate == null) throw new Exception("Novel to update not found.");
                            existingNovelForNotification = new Novel { Title = novelToUpdate.Title }; // Capture old title for notification if needed

                            novelToUpdate.Title = novelUpdateData.Title ?? novelToUpdate.Title;
                            novelToUpdate.Description = novelUpdateData.Description ?? novelToUpdate.Description;
                            novelToUpdate.Genres = novelUpdateData.Genres ?? novelToUpdate.Genres;
                            novelToUpdate.Tags = novelUpdateData.Tags ?? novelToUpdate.Tags;
                            novelToUpdate.Type = novelUpdateData.Type ?? novelToUpdate.Type;
                            novelToUpdate.Format = novelUpdateData.Format ?? novelToUpdate.Format;
                            novelToUpdate.ReleaseYear = novelUpdateData.ReleaseYear ?? novelToUpdate.ReleaseYear;
                            novelToUpdate.AuthorId = novelUpdateData.AuthorId ?? novelToUpdate.AuthorId;
                            novelToUpdate.AlternativeTitles = novelUpdateData.AlternativeTitles ?? novelToUpdate.AlternativeTitles;
                            novelDataForNotification = novelToUpdate; // For notification

                            List<string> pathsFromUpdateRequest = novelUpdateData.Covers ?? new List<string>();
                            List<string> finalUpdatedCoverPaths = new List<string>();
                            List<string> tempFilesToCommitUpdate = new List<string>();

                            foreach (var pathInRequest in pathsFromUpdateRequest)
                            {
                                if (!string.IsNullOrEmpty(pathInRequest))
                                {
                                    if (pathInRequest.Contains("/temp_covers/")) tempFilesToCommitUpdate.Add(pathInRequest);
                                    else finalUpdatedCoverPaths.Add(pathInRequest);
                                }
                            }
                            foreach (var tempPath in tempFilesToCommitUpdate)
                            {
                                string finalPath = await _fileService.CommitTempCoverAsync(tempPath, novelToUpdate.Id);
                                if (!string.IsNullOrEmpty(finalPath)) finalUpdatedCoverPaths.Add(finalPath);
                            }
                            List<string> existingCoversListUpdate = new List<string>();
                            if (!string.IsNullOrWhiteSpace(novelToUpdate.Covers)) { try { existingCoversListUpdate = JsonSerializer.Deserialize<List<string>>(novelToUpdate.Covers); } catch { } }
                            foreach (var oldCoverPath in existingCoversListUpdate)
                            {
                                if (!finalUpdatedCoverPaths.Contains(oldCoverPath)) await _fileService.DeleteCoverAsync(oldCoverPath);
                            }
                            novelToUpdate.Covers = JsonSerializer.Serialize(finalUpdatedCoverPaths.Distinct().ToList());
                            _mySqlService.UpdateNovel(novelToUpdate);
                            break;

                        case ModerationRequestType.NovelDelete:
                            if (!request.NovelId.HasValue) throw new Exception("NovelId is missing for delete request.");
                            var novelToDelete = _mySqlService.GetNovel(request.NovelId.Value);
                            if (novelToDelete != null)
                            {
                                novelDataForNotification = new Novel { Title = novelToDelete.Title }; // Capture title for notification
                                // Also delete covers associated with this novel
                                if (!string.IsNullOrWhiteSpace(novelToDelete.Covers))
                                {
                                    var coversToDelete = JsonSerializer.Deserialize<List<string>>(novelToDelete.Covers);
                                    if (coversToDelete != null)
                                    {
                                        foreach (var coverPath in coversToDelete) await _fileService.DeleteCoverAsync(coverPath);
                                    }
                                }
                                _mySqlService.DeleteNovel(request.NovelId.Value);
                            }
                            else { throw new Exception("Novel to delete not found."); }
                            break;
                        default:
                            throw new InvalidOperationException("Неподдерживаемый тип запроса.");
                    }
                    _mySqlService.UpdateModerationRequest(request);

                    var approvedNovelTitle = novelDataForNotification?.Title ?? existingNovelForNotification?.Title ?? "Неизвестная новелла";
                    var approvedMessage = $"Ваш запрос '{request.RequestType.ToString()}' для новеллы '{approvedNovelTitle}' был одобрен.";
                    _notificationService.CreateNotification(request.UserId, NotificationType.ModerationApproved, approvedMessage, request.NovelId, RelatedItemType.Novel);

                    return Ok(new { success = true, message = $"Запрос ID {requestId} ({request.RequestType.ToString()}) одобрен." });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing approval for request ID {RequestId}", requestId);
                    return StatusCode(500, new { success = false, message = "Ошибка обработки запроса: " + ex.Message });
                }
            }
            else // Reject
            {
                request.Status = ModerationStatus.Rejected;
                _mySqlService.UpdateModerationRequest(request);

                if (request.RequestType == ModerationRequestType.NovelCreate || request.RequestType == ModerationRequestType.NovelUpdate)
                {
                    if (!string.IsNullOrEmpty(request.RequestData))
                    {
                        try
                        {
                            Novel novelDetails = null;
                            // Try deserializing as Novel (for Create) or NovelUpdateRequest (for Update) then get Covers
                            // For simplicity, assuming Covers property is accessible similarly or is a List<string>
                            if (request.RequestType == ModerationRequestType.NovelCreate)
                                novelDetails = JsonSerializer.Deserialize<Novel>(request.RequestData);
                            else if (request.RequestType == ModerationRequestType.NovelUpdate)
                            {
                                // If NovelUpdateRequest has Covers as List<string>
                                var updateReq = JsonSerializer.Deserialize<NovelUpdateRequest>(request.RequestData);
                                if (updateReq != null) novelDetails = new Novel { CoversList = updateReq.Covers }; // Adapt to get CoversList
                            }

                            if (novelDetails != null && novelDetails.CoversList != null)
                            {
                                foreach (var path in novelDetails.CoversList)
                                {
                                    if (!string.IsNullOrEmpty(path) && path.Contains("/temp_covers/"))
                                    {
                                        await _fileService.DeleteCoverAsync(path);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error deleting temporary covers for rejected request ID {RequestId}.", requestId);
                        }
                    }
                }

                string rejectedNovelTitle = "Неизвестная новелла";
                if (request.RequestType == ModerationRequestType.NovelCreate && !string.IsNullOrEmpty(request.RequestData))
                {
                    try { var nt = JsonSerializer.Deserialize<Novel>(request.RequestData); rejectedNovelTitle = nt?.Title; } catch { }
                }
                else if (request.NovelId.HasValue)
                {
                    rejectedNovelTitle = _mySqlService.GetNovel(request.NovelId.Value)?.Title ?? rejectedNovelTitle;
                }
                else if (!string.IsNullOrEmpty(request.RequestData))
                { // Fallback for delete if title was in RequestData
                    try { rejectedNovelTitle = JsonDocument.Parse(request.RequestData).RootElement.GetProperty("Title").GetString() ?? rejectedNovelTitle; } catch { }
                }

                var rejectedMessage = $"Ваш запрос '{request.RequestType.ToString()}' для новеллы '{rejectedNovelTitle}' был отклонен.";
                if (!string.IsNullOrWhiteSpace(moderationComment)) rejectedMessage += $" Причина: {moderationComment}";
                _notificationService.CreateNotification(request.UserId, NotificationType.ModerationRejected, rejectedMessage, request.NovelId, RelatedItemType.Novel);

                return Ok(new { success = true, message = $"Запрос ID {requestId} ({request.RequestType.ToString()}) отклонен." });
            }
        }

        // Chapter Moderation Requests
        public IActionResult ChapterRequestsPartial(int page = 1, int pageSize = 10) // Renamed, added pageSize
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateChapterRequests(currentUser))
            {
                return PartialView("~/Views/Shared/_AccessDeniedPartial.cshtml");
            }

            //pageSize = 10; // Already a parameter
            var chapterRequestTypes = new List<ModerationRequestType> // Updated enum values
                { ModerationRequestType.ChapterCreate, ModerationRequestType.ChapterUpdate, ModerationRequestType.ChapterDelete };

            List<ModerationRequest> rawRequests = _mySqlService.GetPendingModerationRequestsByType(chapterRequestTypes, pageSize, (page - 1) * pageSize);
            int totalRequests = _mySqlService.CountPendingModerationRequestsByType(chapterRequestTypes);

            ViewData["TotalPages"] = (int)Math.Ceiling((double)totalRequests / pageSize);
            ViewData["CurrentPage"] = page;
            ViewData["PageSize"] = pageSize;

            var viewModels = rawRequests.Select(r => {
                var user = _mySqlService.GetUser(r.UserId);
                var novel = r.NovelId.HasValue ? _mySqlService.GetNovel(r.NovelId.Value) : null;
                Chapter proposedChapterData = null;
                ChapterUpdateRequest chapterUpdateData = null;
                Chapter existingChapterData = r.ChapterId.HasValue ? _mySqlService.GetChapter(r.ChapterId.Value) : null;
                string chapterTitle = existingChapterData?.Title;
                string chapterNumber = existingChapterData?.Number;

                if (r.RequestType == ModerationRequestType.ChapterCreate && !string.IsNullOrEmpty(r.RequestData))
                {
                    try
                    {
                        proposedChapterData = JsonSerializer.Deserialize<Chapter>(r.RequestData);
                        chapterTitle = proposedChapterData?.Title;
                        chapterNumber = proposedChapterData?.Number;
                    }
                    catch (JsonException ex) { _logger.LogError(ex, "Failed to deserialize ChapterCreate RequestData for request ID {RequestId}", r.Id); }
                }
                else if (r.RequestType == ModerationRequestType.ChapterUpdate && !string.IsNullOrEmpty(r.RequestData))
                {
                    try
                    {
                        // For display, might show proposed title/number if they are in ChapterUpdateRequest
                        chapterUpdateData = JsonSerializer.Deserialize<ChapterUpdateRequest>(r.RequestData);
                        if (!string.IsNullOrEmpty(chapterUpdateData?.Title)) chapterTitle = chapterUpdateData.Title;
                        if (!string.IsNullOrEmpty(chapterUpdateData?.Number)) chapterNumber = chapterUpdateData.Number;
                    }
                    catch (JsonException ex) { _logger.LogError(ex, "Failed to deserialize ChapterUpdate RequestData for request ID {RequestId}", r.Id); }
                }
                else if (r.RequestType == ModerationRequestType.ChapterDelete && !string.IsNullOrEmpty(r.RequestData))
                {
                    // RequestData might have { "Number": "...", "Title": "..." }
                    try
                    {
                        var doc = JsonDocument.Parse(r.RequestData);
                        if (doc.RootElement.TryGetProperty("Title", out var titleElement)) chapterTitle = titleElement.GetString() ?? chapterTitle;
                        if (doc.RootElement.TryGetProperty("Number", out var numberElement)) chapterNumber = numberElement.GetString() ?? chapterNumber;
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not parse Title/Number from RequestData for ChapterDelete request ID {RequestId}", r.Id); }
                }

                return new ChapterModerationRequestViewModel
                {
                    RequestId = r.Id,
                    RequestType = r.RequestType,
                    UserId = r.UserId,
                    RequesterLogin = user?.Login ?? "N/A",
                    CreatedAt = r.CreatedAt,
                    NovelId = r.NovelId,
                    NovelTitle = novel?.Title ?? "N/A",
                    ChapterId = r.ChapterId,
                    ChapterNumber = chapterNumber ?? "N/A",
                    ChapterTitle = chapterTitle ?? "N/A",
                    RequestDataJson = r.RequestData, // For details view
                    Status = r.Status.ToString()
                };
            }).ToList();

            return PartialView("~/Views/Admin/_ChapterRequestsPartial.cshtml", viewModels);
        }

        public IActionResult ChapterRequestDetails(int requestId)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateChapterRequests(currentUser))
            {
                return RedirectToAction("AccessDenied", "AuthView"); // Or PartialView if used in modal
            }

            ModerationRequest request = _mySqlService.GetModerationRequestById(requestId);
            if (request == null) return NotFound();

            var viewModel = new ChapterModerationRequestViewModel
            {
                RequestId = request.Id,
                RequestType = request.RequestType,
                UserId = request.UserId,
                RequesterLogin = _mySqlService.GetUser(request.UserId)?.Login ?? "N/A",
                CreatedAt = request.CreatedAt,
                NovelId = request.NovelId,
                ChapterId = request.ChapterId,
                RequestDataJson = request.RequestData,
                Status = request.Status.ToString(),
                NovelTitle = request.NovelId.HasValue ? _mySqlService.GetNovel(request.NovelId.Value)?.Title : "N/A",
            };

            if (request.ChapterId.HasValue)
            {
                viewModel.ExistingChapterData = _mySqlService.GetChapter(request.ChapterId.Value);
                if (viewModel.ExistingChapterData != null)
                {
                    viewModel.ChapterNumber = viewModel.ExistingChapterData.Number;
                    viewModel.ChapterTitle = viewModel.ExistingChapterData.Title;
                }
            }

            if ((request.RequestType == ModerationRequestType.ChapterCreate || request.RequestType == ModerationRequestType.ChapterUpdate)
                && !string.IsNullOrEmpty(request.RequestData))
            {
                try
                {
                    viewModel.ProposedChapterData = JsonSerializer.Deserialize<Chapter>(request.RequestData);
                    // Update display title/number if proposed data has them (especially for create)
                    if (request.RequestType == ModerationRequestType.ChapterCreate)
                    {
                        viewModel.ChapterNumber = viewModel.ProposedChapterData?.Number ?? viewModel.ChapterNumber;
                        viewModel.ChapterTitle = viewModel.ProposedChapterData?.Title ?? viewModel.ChapterTitle;
                    }
                }
                catch (JsonException ex) { _logger.LogError(ex, "Failed to deserialize Chapter RequestData for details view, request ID {RequestId}", request.Id); }
            }
            else if (request.RequestType == ModerationRequestType.ChapterDelete && !string.IsNullOrEmpty(request.RequestData))
            {
                if (viewModel.ChapterTitle == null) // If not already set by existing chapter
                {
                    try
                    {
                        var doc = JsonDocument.Parse(request.RequestData);
                        if (doc.RootElement.TryGetProperty("Title", out var titleElement)) viewModel.ChapterTitle = titleElement.GetString();
                        if (doc.RootElement.TryGetProperty("Number", out var numberElement)) viewModel.ChapterNumber = numberElement.GetString();
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not parse Title/Number from RequestData for ChapterDelete details view, request ID {RequestId}", request.Id); }
                }
            }
            if (viewModel.ChapterTitle == null) viewModel.ChapterTitle = "N/A";
            if (viewModel.ChapterNumber == null) viewModel.ChapterNumber = "N/A";

            if (request.ModeratorId.HasValue)
            {
                ViewData["ModeratorUserLogin"] = _mySqlService.GetUser(request.ModeratorId.Value)?.Login ?? "N/A";
            }
            ViewData["ParentNovel"] = viewModel.NovelId.HasValue ? _mySqlService.GetNovel(viewModel.NovelId.Value) : null;


            return View("~/Views/Admin/ChapterRequestDetails.cshtml", viewModel); // Pass ViewModel
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessChapterRequest(int requestId, bool approve, string moderationComment)
        {
            var currentAdminUser = _currentUserService.GetCurrentUser();
            if (currentAdminUser == null || !_permissionService.CanModerateChapterRequests(currentAdminUser))
            {
                return Json(new { success = false, message = "Недостаточно прав." });
            }

            ModerationRequest request = _mySqlService.GetModerationRequestById(requestId);
            if (request == null) return NotFound(new { message = "Request not found." });
            if (request.Status != ModerationStatus.Pending) return BadRequest(new { message = "Запрос уже обработан." });

            request.ModeratorId = currentAdminUser.Id;
            request.ModerationComment = moderationComment;
            request.UpdatedAt = DateTime.UtcNow;

            Chapter chapterDataForNotification = null;
            Chapter existingChapterForNotification = null;
            string novelTitleForNotification = (request.NovelId.HasValue ? _mySqlService.GetNovel(request.NovelId.Value)?.Title : null) ?? "Неизвестная новелла";

            if (approve)
            {
                request.Status = ModerationStatus.Approved;
                try
                {
                    switch (request.RequestType)
                    {
                        case ModerationRequestType.ChapterCreate:
                            var chapterCreateData = JsonSerializer.Deserialize<Chapter>(request.RequestData);
                            if (chapterCreateData == null) throw new JsonException("Failed to deserialize ChapterCreate data.");

                            if (!request.NovelId.HasValue && chapterCreateData.NovelId == 0) throw new Exception("NovelId for chapter is missing.");
                            chapterCreateData.NovelId = request.NovelId ?? chapterCreateData.NovelId;
                            chapterCreateData.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            chapterCreateData.CreatorId = request.UserId;
                            _mySqlService.CreateChapter(chapterCreateData);
                            chapterDataForNotification = chapterCreateData;

                            if (request.NovelId.HasValue)
                            { // Auto add TranslatorId to Novel
                                var novelForUpdate = _mySqlService.GetNovel(request.NovelId.Value);
                                if (novelForUpdate != null)
                                {
                                    if (novelForUpdate.TranslatorIds == null)
                                    {
                                        novelForUpdate.TranslatorIds = new List<int>();
                                    }
                                    if (!novelForUpdate.TranslatorIds.Contains(request.UserId)) // request.UserId is int
                                    {
                                        novelForUpdate.TranslatorIds.Add(request.UserId);
                                        _mySqlService.UpdateNovel(novelForUpdate); // UpdateNovel will handle serialization
                                    }
                                }
                            }
                            break;

                        case ModerationRequestType.ChapterUpdate:
                            var chapterUpdateData = JsonSerializer.Deserialize<ChapterUpdateRequest>(request.RequestData);
                            if (chapterUpdateData == null) throw new JsonException("Failed to deserialize ChapterUpdate data.");

                            var chapterToUpdate = _mySqlService.GetChapter(request.ChapterId.Value);
                            if (chapterToUpdate == null) throw new Exception("Chapter to update not found.");
                            existingChapterForNotification = new Chapter { Title = chapterToUpdate.Title, Number = chapterToUpdate.Number };

                            chapterToUpdate.Number = chapterUpdateData.Number ?? chapterToUpdate.Number;
                            chapterToUpdate.Title = chapterUpdateData.Title ?? chapterToUpdate.Title;
                            chapterToUpdate.Content = chapterUpdateData.Content ?? chapterToUpdate.Content;
                            chapterToUpdate.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); // Update date on edit
                            _mySqlService.UpdateChapter(chapterToUpdate);
                            chapterDataForNotification = chapterToUpdate;
                            break;

                        case ModerationRequestType.ChapterDelete:
                            if (!request.ChapterId.HasValue) throw new Exception("ChapterId is missing for delete request.");
                            var chapterToDelete = _mySqlService.GetChapter(request.ChapterId.Value);
                            if (chapterToDelete != null)
                            {
                                chapterDataForNotification = new Chapter { Title = chapterToDelete.Title, Number = chapterToDelete.Number };
                                int novelIdForDeleteCheck = chapterToDelete.NovelId; // Use chapter's novelId
                                int userIdForDeleteCheck = chapterToDelete.CreatorId.Value; // Use chapter's creatorId

                                _mySqlService.DeleteChapter(request.ChapterId.Value);

                                var novelAfterDelete = _mySqlService.GetNovel(novelIdForDeleteCheck); // Auto remove TranslatorId from Novel
                                if (novelAfterDelete != null)
                                {
                                    // Check if there are any OTHER chapters by this user for this novel
                                    var remainingChaptersByThisUser = _mySqlService.GetChaptersByNovel(novelIdForDeleteCheck)
                                                                         .Any(c => c.CreatorId == userIdForDeleteCheck); // Simpler check: any chapter by this user remains

                                    if (!remainingChaptersByThisUser && novelAfterDelete.TranslatorIds != null && novelAfterDelete.TranslatorIds.Contains(userIdForDeleteCheck))
                                    {
                                        novelAfterDelete.TranslatorIds.Remove(userIdForDeleteCheck);
                                        _mySqlService.UpdateNovel(novelAfterDelete); // UpdateNovel will handle serialization
                                    }
                                }
                            }
                            else { throw new Exception("Chapter to delete not found."); }
                            break;
                        default:
                            throw new InvalidOperationException("Неподдерживаемый тип запроса для глав.");
                    }
                    _mySqlService.UpdateModerationRequest(request);

                    string approvedChapterTitle = chapterDataForNotification?.Title ?? existingChapterForNotification?.Title ?? "Неизвестная глава";
                    string approvedMessage = $"Ваш запрос '{request.RequestType.ToString()}' для главы '{approvedChapterTitle}' (новелла '{novelTitleForNotification}') был одобрен.";

                    int? approvedRelatedChapterId = request.ChapterId;
                    if (request.RequestType == ModerationRequestType.ChapterCreate && chapterDataForNotification != null)
                    {
                        var newChapter = _mySqlService.GetChaptersByNovel(chapterDataForNotification.NovelId)
                                             .FirstOrDefault(c => c.Title == chapterDataForNotification.Title &&
                                                                  c.Number == chapterDataForNotification.Number &&
                                                                  c.CreatorId == chapterDataForNotification.CreatorId);
                        if (newChapter != null) approvedRelatedChapterId = newChapter.Id;
                    }
                    _notificationService.CreateNotification(request.UserId, NotificationType.ModerationApproved, approvedMessage, approvedRelatedChapterId ?? request.NovelId, RelatedItemType.Chapter);

                    if (request.RequestType == ModerationRequestType.ChapterCreate && request.NovelId.HasValue && chapterDataForNotification != null)
                    {
                        var newChapterForSubscribers = _mySqlService.GetChaptersByNovel(request.NovelId.Value)
                                             .FirstOrDefault(c => c.Title == chapterDataForNotification.Title && c.Number == chapterDataForNotification.Number && c.CreatorId == request.UserId);
                        if (newChapterForSubscribers != null)
                        {
                            var subscribers = _mySqlService.GetUserIdsSubscribedToNovel(request.NovelId.Value, new List<string> { "reading", "read", "favorites" });
                            var newChapterMessage = $"Новая глава '{newChapterForSubscribers.Number} - {newChapterForSubscribers.Title}' добавлена к новелле '{novelTitleForNotification}'.";
                            foreach (var subId in subscribers)
                            {
                                if (subId != request.UserId)
                                {
                                    _notificationService.CreateNotification(subId, NotificationType.NewChapter, newChapterMessage, newChapterForSubscribers.Id, RelatedItemType.Chapter);
                                }
                            }
                        }
                    }
                    return Ok(new { success = true, message = $"Запрос главы ID {requestId} ({request.RequestType.ToString()}) одобрен." });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing chapter approval for request ID {RequestId}", requestId);
                    return StatusCode(500, new { success = false, message = "Ошибка обработки запроса главы: " + ex.Message });
                }
            }
            else // Reject
            {
                request.Status = ModerationStatus.Rejected;
                _mySqlService.UpdateModerationRequest(request);

                string rejectedChapterTitle = "Неизвестная глава";
                if (request.RequestType == ModerationRequestType.ChapterCreate && !string.IsNullOrEmpty(request.RequestData))
                {
                    try { var ch = JsonSerializer.Deserialize<Chapter>(request.RequestData); rejectedChapterTitle = ch?.Title; } catch { }
                }
                else if (request.ChapterId.HasValue)
                {
                    var existingCh = _mySqlService.GetChapter(request.ChapterId.Value); rejectedChapterTitle = existingCh?.Title ?? rejectedChapterTitle;
                }
                else if (!string.IsNullOrEmpty(request.RequestData))
                {
                    try { rejectedChapterTitle = JsonDocument.Parse(request.RequestData).RootElement.GetProperty("Title").GetString() ?? rejectedChapterTitle; } catch { }
                }

                var rejectedMessage = $"Ваш запрос '{request.RequestType.ToString()}' для главы '{rejectedChapterTitle}' (новелла '{novelTitleForNotification}') был отклонен.";
                if (!string.IsNullOrWhiteSpace(moderationComment)) rejectedMessage += $" Причина: {moderationComment}";
                _notificationService.CreateNotification(request.UserId, NotificationType.ModerationRejected, rejectedMessage, request.ChapterId ?? request.NovelId, RelatedItemType.Chapter);

                return Ok(new { success = true, message = $"Запрос главы ID {requestId} ({request.RequestType.ToString()}) отклонен." });
            }
        }
    }
}
