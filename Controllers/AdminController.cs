using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;
using BulbaLib.Models; // For ViewModels, DB Models
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.IO;
using System.Text.RegularExpressions;
using System;
using BulbaLib.Services; // Replaced BulbaLib.Interfaces
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace BulbaLib.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly MySqlService _mySqlService;
        private readonly PermissionService _permissionService;
        private readonly ICurrentUserService _currentUserService;
        private readonly INotificationService _notificationService;
        private readonly FileService _fileService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            MySqlService mySqlService, PermissionService permissionService, ICurrentUserService currentUserService,
            INotificationService notificationService, FileService fileService, ILogger<AdminController> logger)
        {
            _mySqlService = mySqlService; _permissionService = permissionService; _currentUserService = currentUserService;
            _notificationService = notificationService; _fileService = fileService; _logger = logger;
        }

        private string GetActionDisplayName(ModerationRequestType requestType)
        {
            var memberInfo = typeof(ModerationRequestType).GetMember(requestType.ToString()).FirstOrDefault();
            if (memberInfo != null)
            {
                var displayAttribute = memberInfo.GetCustomAttribute<DisplayAttribute>();
                if (displayAttribute != null)
                {
                    string name = displayAttribute.Name;
                    if (name.Contains(" новеллы"))
                    {
                        name = name.Substring(0, name.IndexOf(" новеллы", StringComparison.OrdinalIgnoreCase));
                    }
                    else if (name.Contains(" главы"))
                    {
                        name = name.Substring(0, name.IndexOf(" главы", StringComparison.OrdinalIgnoreCase));
                    }
                    return name.ToLowerInvariant();
                }
            }
            return requestType.ToString().ToLowerInvariant();
        }

        public IActionResult Index()
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanViewAdminPanel(currentUser))
            { return RedirectToAction("AccessDenied", "AuthView"); }
            return View("~/Views/Admin/Index.cshtml");
        }

        public IActionResult UsersPartial(string searchTerm = null) // Added searchTerm parameter
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanManageUsers(currentUser))
            { return PartialView("~/Views/Shared/_AccessDeniedPartial.cshtml"); }

            List<User> users;
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                users = _mySqlService.GetAllUsers().OrderBy(u => u.Id).ToList(); // Получаем всех и сортируем по ID
            }
            else
            {
                // Используем новый метод для поиска с полной информацией о пользователе и сортируем по ID
                users = _mySqlService.SearchUsersForAdmin(searchTerm).OrderBy(u => u.Id).ToList();
            }

            var viewModel = new AdminUsersViewModel
            {
                Users = users,
                SearchTerm = searchTerm
            };

            return PartialView("~/Views/Admin/_UsersPartial.cshtml", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateUserRole(int userId, string newRole)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            User targetUser = _mySqlService.GetUser(userId);
            if (targetUser == null) return Json(new { success = false, message = "User not found." });
            if (currentUser != null && currentUser.Id == targetUser.Id)
            { return Json(new { success = false, message = "Администратор не может изменить собственную роль." }); }
            if (Enum.TryParse<UserRole>(newRole, true, out UserRole roleEnum))
            {
                _mySqlService.UpdateUserRole(userId, roleEnum.ToString());
                _logger.LogInformation("User {UserId} role updated to {NewRole} by Admin {AdminUserId}", userId, roleEnum.ToString(), currentUser?.Id);
                return Json(new { success = true, message = "User role updated successfully." });
            }
            return Json(new { success = false, message = "Invalid role specified." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleUserBlock(int userId)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            User targetUser = _mySqlService.GetUser(userId);
            if (targetUser == null) return Json(new { success = false, message = "User not found." });
            if (currentUser != null && currentUser.Id == targetUser.Id)
            { return Json(new { success = false, message = "Администратор не может заблокировать сам себя." }); }
            bool newBlockStatus = !targetUser.IsBlocked;
            _mySqlService.SetUserBlockedStatus(userId, newBlockStatus);
            _logger.LogInformation("User {TargetUserId} IsBlocked status set to {NewBlockStatus} by Admin {AdminUserId}", userId, newBlockStatus, currentUser?.Id);
            return Json(new { success = true, message = $"User {(newBlockStatus ? "blocked" : "unblocked")} successfully.", isBlocked = newBlockStatus });
        }

        public IActionResult NovelRequestsPartial(int page = 1, int pageSize = 10)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateNovelRequests(currentUser))
            { return PartialView("~/Views/Shared/_AccessDeniedPartial.cshtml"); }
            var novelRequestTypes = new List<ModerationRequestType> { ModerationRequestType.AddNovel, ModerationRequestType.EditNovel, ModerationRequestType.DeleteNovel };
            List<ModerationRequest> rawRequests = _mySqlService.GetPendingModerationRequestsByType(novelRequestTypes, pageSize, (page - 1) * pageSize);
            var viewModels = rawRequests.Select(r => {
                var user = _mySqlService.GetUser(r.UserId);
                string novelTitle = r.NovelId.HasValue ? _mySqlService.GetNovel(r.NovelId.Value)?.Title : null;
                Novel proposedData = null; NovelUpdateRequest changesData = null;
                if (r.RequestType == ModerationRequestType.AddNovel && !string.IsNullOrEmpty(r.RequestData))
                {
                    try { proposedData = JsonSerializer.Deserialize<Novel>(r.RequestData); novelTitle = proposedData?.Title ?? "Новая новелла"; } catch (JsonException ex) { _logger.LogError(ex, "NRP: AddNovel Deser Error ID {Rid}", r.Id); }
                }
                else if (r.RequestType == ModerationRequestType.EditNovel)
                {
                    if (novelTitle == null && r.NovelId.HasValue) novelTitle = _mySqlService.GetNovel(r.NovelId.Value)?.Title;
                    if (!string.IsNullOrEmpty(r.RequestData)) { try { changesData = JsonSerializer.Deserialize<NovelUpdateRequest>(r.RequestData); } catch (JsonException ex) { _logger.LogError(ex, "NRP: EditNovel Deser Error ID {Rid}", r.Id); } }
                }
                else if (r.RequestType == ModerationRequestType.DeleteNovel)
                {
                    if (novelTitle == null && r.NovelId.HasValue) novelTitle = _mySqlService.GetNovel(r.NovelId.Value)?.Title;
                    if (novelTitle == null && !string.IsNullOrEmpty(r.RequestData)) { try { novelTitle = JsonDocument.Parse(r.RequestData).RootElement.GetProperty("Title").GetString(); } catch { } }
                }
                return new NovelModerationRequestViewModel
                {
                    RequestId = r.Id,
                    RequestType = r.RequestType,
                    UserId = r.UserId,
                    RequesterLogin = user?.Login ?? "N/A",
                    CreatedAt = r.CreatedAt,
                    NovelId = r.NovelId,
                    NovelTitle = novelTitle ?? "N/A",
                    RequestDataJson = r.RequestData,
                    ProposedNovelData = proposedData,
                    ProposedNovelChanges = changesData,
                    Status = r.Status.ToString()
                };
            }).ToList();
            int totalRequests = _mySqlService.CountPendingModerationRequestsByType(novelRequestTypes);
            ViewData["TotalPages"] = (int)Math.Ceiling((double)totalRequests / pageSize); ViewData["CurrentPage"] = page; ViewData["PageSize"] = pageSize;
            return PartialView("~/Views/Admin/_NovelRequestsPartial.cshtml", viewModels);
        }

        public IActionResult NovelRequestDetails(int requestId)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateNovelRequests(currentUser))
            { return RedirectToAction("AccessDenied", "AuthView"); }
            ModerationRequest request = _mySqlService.GetModerationRequestById(requestId);
            if (request == null) return NotFound("Запрос не найден.");

            ViewData["RequesterUser"] = _mySqlService.GetUser(request.UserId);
            if (request.ModeratorId.HasValue) { ViewData["ModeratorUserLogin"] = _mySqlService.GetUser(request.ModeratorId.Value)?.Login ?? "N/A"; }

            Novel requestedNovelForView = null; Novel existingNovelForView = null;
            if (request.RequestType == ModerationRequestType.AddNovel && !string.IsNullOrEmpty(request.RequestData))
            {
                try { requestedNovelForView = JsonSerializer.Deserialize<Novel>(request.RequestData); }
                catch (JsonException ex) { _logger.LogError(ex, "NRD: AddNovel Deser Error ID {Rid}", request.Id); ViewData["DeserializationError"] = "Ошибка чтения данных (AddNovel)."; }
            }
            else if (request.RequestType == ModerationRequestType.EditNovel)
            {
                if (request.NovelId.HasValue) existingNovelForView = _mySqlService.GetNovel(request.NovelId.Value);
                if (!string.IsNullOrEmpty(request.RequestData))
                {
                    try { var dto = JsonSerializer.Deserialize<NovelUpdateRequest>(request.RequestData); if (dto != null) requestedNovelForView = new Novel { Title = dto.Title, Description = dto.Description, Covers = JsonSerializer.Serialize(dto.Covers), Genres = dto.Genres, Tags = dto.Tags, Type = dto.Type, Format = dto.Format, ReleaseYear = dto.ReleaseYear, AlternativeTitles = dto.AlternativeTitles, AuthorId = dto.AuthorId }; }
                    catch (JsonException ex) { _logger.LogError(ex, "NRD: EditNovel Deser Error ID {Rid}", request.Id); ViewData["DeserializationError"] = "Ошибка чтения изменений (EditNovel)."; }
                }
            }
            else if (request.RequestType == ModerationRequestType.DeleteNovel && request.NovelId.HasValue)
            {
                existingNovelForView = _mySqlService.GetNovel(request.NovelId.Value);
            }
            ViewData["RequestedNovel"] = requestedNovelForView; ViewData["ExistingNovel"] = existingNovelForView;
            return View("~/Views/Admin/NovelRequestDetails.cshtml", request);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessNovelRequest(int requestId, bool approve, string moderationComment)
        {
            var currentAdminUser = _currentUserService.GetCurrentUser();
            if (currentAdminUser == null || !_permissionService.CanModerateNovelRequests(currentAdminUser))
            { return Json(new { success = false, message = "Недостаточно прав." }); }
            ModerationRequest request = _mySqlService.GetModerationRequestById(requestId);
            if (request == null) return Json(new { success = false, message = "Request not found." });
            if (request.Status != ModerationStatus.Pending) return Json(new { success = false, message = "Запрос уже обработан." });
            request.ModeratorId = currentAdminUser.Id; request.ModerationComment = moderationComment; request.UpdatedAt = DateTime.UtcNow;
            Novel novelForNotification = null; string originalNovelTitleForNotification = null;
            if (approve)
            {
                request.Status = ModerationStatus.Approved;
                try
                {
                    switch (request.RequestType)
                    {
                        case ModerationRequestType.AddNovel:
                            var createData = JsonSerializer.Deserialize<Novel>(request.RequestData); if (createData == null) throw new JsonException("Null data AddNovel");
                            novelForNotification = createData;
                            // createData.AuthorId = request.UserId; // AuthorId is now original author from data
                            createData.CreatorId = request.UserId; // UserId (who sent the request) is the CreatorId
                            createData.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            createData.Status = NovelStatus.Approved;
                            var dbNovel = new Novel
                            {
                                Title = createData.Title,
                                Description = createData.Description,
                                AuthorId = createData.AuthorId, // This is the original author, from the submitted data
                                CreatorId = createData.CreatorId, // This is the user who submitted the request
                                Genres = createData.Genres,
                                Tags = createData.Tags,
                                Type = createData.Type,
                                Format = createData.Format,
                                ReleaseYear = createData.ReleaseYear,
                                AlternativeTitles = createData.AlternativeTitles,
                                RelatedNovelIds = createData.RelatedNovelIds,
                                Date = createData.Date,
                                Status = createData.Status,
                                Covers = "[]"
                            };
                            int newId = _mySqlService.CreateNovel(dbNovel);
                            List<string> tempP = new List<string>(); if (!string.IsNullOrEmpty(createData.Covers)) { try { tempP = JsonSerializer.Deserialize<List<string>>(createData.Covers); } catch { } }
                            List<string> finalP = new List<string>(); if (tempP != null) { foreach (var p in tempP) { if (!string.IsNullOrEmpty(p) && p.Contains("/temp_covers/")) { string fp = await _fileService.CommitTempCoverAsync(p, newId); if (!string.IsNullOrEmpty(fp)) finalP.Add(fp); } else if (!string.IsNullOrEmpty(p)) { finalP.Add(p); } } }
                            if (finalP.Any()) { var utN = _mySqlService.GetNovel(newId); if (utN != null) { utN.Covers = JsonSerializer.Serialize(finalP); _mySqlService.UpdateNovel(utN); } }
                            request.NovelId = newId; break;
                        case ModerationRequestType.EditNovel:
                            var moderationPayload = JsonSerializer.Deserialize<NovelEditModerationData>(request.RequestData);
                            if (moderationPayload == null || moderationPayload.UpdatedFields == null) throw new JsonException("Invalid or null data for EditNovel moderation.");

                            var novelToUpdate = _mySqlService.GetNovel(request.NovelId.Value);
                            if (novelToUpdate == null) throw new Exception($"Novel with Id {request.NovelId.Value} not found for update.");

                            originalNovelTitleForNotification = novelToUpdate.Title;
                            novelForNotification = novelToUpdate; // For notification context

                            // Apply updated fields from moderationPayload.UpdatedFields
                            novelToUpdate.Title = moderationPayload.UpdatedFields.Title ?? novelToUpdate.Title;
                            novelToUpdate.Description = moderationPayload.UpdatedFields.Description ?? novelToUpdate.Description;
                            novelToUpdate.Genres = moderationPayload.UpdatedFields.Genres ?? novelToUpdate.Genres; // Already processed JSON string
                            novelToUpdate.Tags = moderationPayload.UpdatedFields.Tags ?? novelToUpdate.Tags;       // Already processed JSON string
                            novelToUpdate.Type = moderationPayload.UpdatedFields.Type ?? novelToUpdate.Type;
                            novelToUpdate.Format = moderationPayload.UpdatedFields.Format ?? novelToUpdate.Format;
                            novelToUpdate.ReleaseYear = moderationPayload.UpdatedFields.ReleaseYear ?? novelToUpdate.ReleaseYear;
                            novelToUpdate.AuthorId = moderationPayload.UpdatedFields.AuthorId ?? novelToUpdate.AuthorId;
                            novelToUpdate.AlternativeTitles = moderationPayload.UpdatedFields.AlternativeTitles ?? novelToUpdate.AlternativeTitles;
                            novelToUpdate.RelatedNovelIds = moderationPayload.UpdatedFields.RelatedNovelIds ?? novelToUpdate.RelatedNovelIds;

                            // Revised Cover Handling
                            var keptCovers = moderationPayload.KeptExistingCovers ?? new List<string>();
                            var tempCoversToCommit = moderationPayload.NewTempCoverPaths ?? new List<string>();

                            List<string> finalNovelCoverPaths = new List<string>(keptCovers);

                            foreach (var tempPath in tempCoversToCommit)
                            {
                                if (!string.IsNullOrEmpty(tempPath) && tempPath.Contains("/temp_covers/"))
                                {
                                    string committedPath = await _fileService.CommitTempCoverAsync(tempPath, novelToUpdate.Id);
                                    if (!string.IsNullOrEmpty(committedPath))
                                    {
                                        finalNovelCoverPaths.Add(committedPath);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Failed to commit temporary cover {TempPath} for novel {NovelId} during admin approval.", tempPath, novelToUpdate.Id);
                                    }
                                }
                            }

                            List<string> currentDatabaseCovers = new List<string>();
                            if (!string.IsNullOrWhiteSpace(novelToUpdate.Covers))
                            {
                                try { currentDatabaseCovers = JsonSerializer.Deserialize<List<string>>(novelToUpdate.Covers); }
                                catch (JsonException ex) { _logger.LogError(ex, "Error deserializing current novel covers for novel {NovelId}", novelToUpdate.Id); }
                            }

                            foreach (var dbCoverPath in currentDatabaseCovers)
                            {
                                if (!finalNovelCoverPaths.Contains(dbCoverPath))
                                {
                                    _logger.LogInformation("Admin approval: Deleting cover {CoverPath} as it's not in the final list for novel {NovelId}.", dbCoverPath, novelToUpdate.Id);
                                    await _fileService.DeleteCoverAsync(dbCoverPath);
                                }
                            }

                            novelToUpdate.Covers = JsonSerializer.Serialize(finalNovelCoverPaths.Distinct().ToList());
                            _mySqlService.UpdateNovel(novelToUpdate);
                            break;
                        case ModerationRequestType.DeleteNovel:
                            int? novelIdToDelete = request.NovelId;
                            if (!novelIdToDelete.HasValue && !string.IsNullOrEmpty(request.RequestData))
                            {
                                try
                                {
                                    var requestDataJson = JsonDocument.Parse(request.RequestData).RootElement;
                                    if (requestDataJson.TryGetProperty("NovelId", out var novelIdElement) && novelIdElement.ValueKind == JsonValueKind.Number)
                                    {
                                        novelIdToDelete = novelIdElement.GetInt32();
                                    }
                                    else if (requestDataJson.TryGetProperty("Id", out var idElement) && idElement.ValueKind == JsonValueKind.Number) // Fallback for "Id"
                                    {
                                        novelIdToDelete = idElement.GetInt32();
                                    }
                                }
                                catch (JsonException ex)
                                {
                                    _logger.LogWarning(ex, "Failed to parse NovelId from RequestData for DeleteNovel request ID {RequestId}", requestId);
                                }
                            }

                            if (!novelIdToDelete.HasValue)
                            {
                                _logger.LogError("NovelId missing for DeleteNovel request ID {RequestId}. Cannot process deletion.", requestId);
                                throw new Exception("NovelId is missing and could not be retrieved from RequestData. Cannot process deletion.");
                            }

                            var novelTD = _mySqlService.GetNovel(novelIdToDelete.Value);
                            if (novelTD != null)
                            {
                                novelForNotification = new Novel { Title = novelTD.Title };
                                if (!string.IsNullOrWhiteSpace(novelTD.Covers))
                                {
                                    var cTD = JsonSerializer.Deserialize<List<string>>(novelTD.Covers);
                                    if (cTD != null) { foreach (var cP in cTD) await _fileService.DeleteCoverAsync(cP); }
                                }
                                _mySqlService.DeleteNovel(novelIdToDelete.Value);
                                // Ensure the request object's NovelId is updated if it was parsed from RequestData, for notification consistency
                                if (!request.NovelId.HasValue) request.NovelId = novelIdToDelete;
                            }
                            else
                            {
                                _logger.LogWarning("Novel {Nid} for deletion not found, request ID {RequestId}", novelIdToDelete.Value, requestId);
                            }
                            break;
                        default: throw new InvalidOperationException("Unsupported type");
                    }
                    _mySqlService.UpdateModerationRequest(request); // This updates status, moderatorId, comment, updatedAt
                    string actionDisplayName = GetActionDisplayName(request.RequestType);
                    var finalNT = novelForNotification?.Title ?? originalNovelTitleForNotification ?? "?";
                    _notificationService.CreateNotification(request.UserId, NotificationType.RequestApproved, $"Запрос на {actionDisplayName} новеллы '{finalNT}' одобрен.", request.NovelId, RelatedItemType.Novel);

                    return Json(new { success = true, message = $"Запрос ID {requestId} ({request.RequestType}) одобрен." });
                }
                catch (Exception ex) { _logger.LogError(ex, "Error approving req ID {Rid}", requestId); return Json(new { success = false, message = "Ошибка одобрения запроса" }); }
            }
            else // Reject
            {
                // request.Status = ModerationStatus.Rejected; // This is handled by UpdateModerationRequestStatusAsync
                await _mySqlService.UpdateModerationRequestStatusAsync(request.Id, ModerationStatus.Rejected, currentAdminUser.Id, moderationComment);
                // request.RejectionReason = moderationComment; // This is handled by UpdateModerationRequestStatusAsync

                if (request.RequestType == ModerationRequestType.AddNovel || request.RequestType == ModerationRequestType.EditNovel)
                {
                    if (!string.IsNullOrEmpty(request.RequestData))
                    {
                        try
                        {
                            List<string> tempPathsToDel = new List<string>();
                            if (request.RequestType == ModerationRequestType.AddNovel) { var d = JsonSerializer.Deserialize<Novel>(request.RequestData); if (!string.IsNullOrEmpty(d?.Covers)) tempPathsToDel = JsonSerializer.Deserialize<List<string>>(d.Covers); }
                            else { var ud = JsonSerializer.Deserialize<NovelUpdateRequest>(request.RequestData); if (ud?.Covers != null) tempPathsToDel = ud.Covers; }
                            foreach (var p in tempPathsToDel) { if (!string.IsNullOrEmpty(p) && p.Contains("/temp_covers/")) { await _fileService.DeleteCoverAsync(p); } }
                        }
                        catch (Exception ex) { _logger.LogError(ex, "Error deleting temp covers for rejected req ID {Rid}", requestId); }
                    }
                }
                string actionDisplayName = GetActionDisplayName(request.RequestType);
                string rejNT = "?";
                if (request.RequestType == ModerationRequestType.AddNovel && !string.IsNullOrEmpty(request.RequestData))
                {
                    try { var nt = JsonSerializer.Deserialize<Novel>(request.RequestData); rejNT = nt?.Title ?? rejNT; } catch { }
                }
                else if (request.NovelId.HasValue)
                {
                    rejNT = _mySqlService.GetNovel(request.NovelId.Value)?.Title ?? rejNT;
                }
                else if (request.RequestType == ModerationRequestType.DeleteNovel && !string.IsNullOrEmpty(request.RequestData))
                {
                    try { rejNT = JsonDocument.Parse(request.RequestData).RootElement.GetProperty("Title").GetString() ?? rejNT; } catch { }
                }
                var rejMsg = $"Запрос на {actionDisplayName} новеллы '{rejNT}' отклонен.";
                // Причина (moderationComment) теперь будет в отдельном поле в UI, если есть.
                // Основное сообщение не включает причину, но уведомление будет ссылаться на ModerationRequest, где причина хранится.
                // Передаем moderationComment как причину в уведомление
                _notificationService.CreateNotification(request.UserId, NotificationType.RequestRejected, rejMsg, request.Id, RelatedItemType.ModerationRequest, moderationComment);
                return Json(new { success = true, message = $"Запрос ID {requestId} ({request.RequestType}) отклонен." });
            }
        }

        public IActionResult ChapterRequestsPartial(int page = 1, int pageSize = 10) // pageSize can be used for pagination if needed later
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateChapterRequests(currentUser))
            {
                return PartialView("~/Views/Shared/_AccessDeniedPartial.cshtml");
            }

            // Fetch data using the new service method
            // The new service method doesn't support pagination directly yet, so we fetch all and then paginate in memory if needed,
            // or modify the service method later. For now, let's assume we get all relevant requests.
            List<ChapterModerationRequestViewModel> viewModels = _mySqlService.GetPendingChapterModerationRequestsWithDetails();

            // Manual pagination (if service doesn't support it)
            int totalReqs = viewModels.Count;
            var paginatedViewModels = viewModels.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            ViewData["TotalPages"] = (int)Math.Ceiling((double)totalReqs / pageSize);
            ViewData["CurrentPage"] = page;
            ViewData["PageSize"] = pageSize; // For pagination controls in the partial view

            // Pass the paginated list to the partial view
            return PartialView("~/Views/Admin/_ChapterRequestsPartial.cshtml", paginatedViewModels);
        }

        // Corrected the typo from MySqlService (proposed_Title to proposedTitle) in the view model mapping within MySqlService.
        // This controller method will call the updated MySqlService method.
        public async Task<IActionResult> ChapterRequestDetails(int requestId)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateChapterRequests(currentUser)) { return RedirectToAction("AccessDenied", "AuthView"); }
            ModerationRequest request = _mySqlService.GetModerationRequestById(requestId);
            if (request == null) return NotFound("Запрос не найден.");

            var requesterUser = _mySqlService.GetUser(request.UserId);
            var parentNovel = request.NovelId.HasValue ? _mySqlService.GetNovel(request.NovelId.Value) : null;

            var viewModel = new ChapterModerationRequestViewModel
            {
                RequestId = request.Id,
                RequestType = request.RequestType,
                UserId = request.UserId,
                RequesterLogin = requesterUser?.Login ?? "N/A",
                CreatedAt = request.CreatedAt,
                NovelId = request.NovelId ?? 0, // Safely handle nullable NovelId
                NovelTitle = parentNovel?.Title ?? "N/A",
                ChapterId = request.ChapterId,
                RequestDataJson = request.RequestData,
                Status = request.Status.ToString()
            };

            if (request.ChapterId.HasValue && (request.RequestType == ModerationRequestType.EditChapter || request.RequestType == ModerationRequestType.DeleteChapter))
            {
                viewModel.ExistingChapterData = await _mySqlService.GetChapterAsync(request.ChapterId.Value);
                if (viewModel.ExistingChapterData != null)
                {
                    viewModel.ChapterNumber = viewModel.ExistingChapterData.Number; viewModel.ChapterTitle = viewModel.ExistingChapterData.Title;
                    // Content is now loaded by GetChapterAsync, direct file path reconstruction here for content might be redundant
                    // if GetChapterAsync populates viewModel.ExistingChapterData.Content directly.
                    // For now, we assume GetChapterAsync populates .Content, so ExistingContent can use it.
                    // If GetChapterAsync does NOT populate .Content, then the ReadChapterContentAsync call might still be needed
                    // using viewModel.ExistingChapterData.ContentFilePath.
                    // Given the previous subtask, GetChapterAsync *should* load the content.
                    viewModel.ExistingContent = viewModel.ExistingChapterData.Content ?? "[Не удалось загрузить существующий контент или контент пуст]";
                }
                else { viewModel.ExistingContent = "[Существующая глава не найдена]"; }
            }
            if ((request.RequestType == ModerationRequestType.AddChapter || request.RequestType == ModerationRequestType.EditChapter) && !string.IsNullOrEmpty(request.RequestData))
            {
                try
                {
                    var pData = JsonSerializer.Deserialize<Chapter>(request.RequestData); viewModel.ProposedChapterData = pData; viewModel.ProposedContent = pData?.Content;
                    if (pData != null) { viewModel.ChapterNumber = pData.Number; viewModel.ChapterTitle = pData.Title; }
                }
                catch (JsonException ex) { _logger.LogError(ex, "CRD: Deser Error ID {Rid}", request.Id); ViewData["DeserializationError"] = "Ошибка чтения данных: " + ex.Message; viewModel.ProposedContent = "[Ошибка десериализации]"; }
            }
            if (string.IsNullOrEmpty(viewModel.ChapterNumber)) viewModel.ChapterNumber = viewModel.ExistingChapterData?.Number ?? "N/A";
            if (string.IsNullOrEmpty(viewModel.ChapterTitle)) viewModel.ChapterTitle = viewModel.ExistingChapterData?.Title ?? "N/A";
            // The MySqlService method GetChapterModerationRequestDetailsByIdAsync now populates ProposedChapterContent and CurrentChapterContent.
            // So, no need to manually read file content here.

            return View("~/Views/Admin/ChapterRequestDetails.cshtml", viewModel);
        }

        public async Task<IActionResult> ChapterRequestPreview(int requestId)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateChapterRequests(currentUser)) // Assuming same permission as details
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            ModerationRequest request = await _mySqlService.GetModerationRequestByIdAsync(requestId);
            if (request == null)
            {
                return NotFound("Запрос на модерацию не найден.");
            }

            if (!request.NovelId.HasValue)
            {
                _logger.LogWarning("ChapterRequestPreview: ModerationRequest {RequestId} does not have a NovelId.", requestId);
                return BadRequest("Запрос не связан с новеллой.");
            }

            Novel parentNovel = _mySqlService.GetNovel(request.NovelId.Value);
            if (parentNovel == null)
            {
                return NotFound("Связанная новелла не найдена.");
            }

            string chapterContentForPreview = "[Контент не определен]";
            string chapterNumber = "N/A";
            string chapterTitle = "N/A";

            Chapter proposedChapterData = null;
            Chapter existingChapterData = null;

            if (!string.IsNullOrEmpty(request.RequestData))
            {
                try
                {
                    proposedChapterData = JsonSerializer.Deserialize<Chapter>(request.RequestData, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "ChapterRequestPreview: Error deserializing RequestData for request ID {RequestId}", requestId);
                    // Continue, but content might be unavailable or fallback
                }
            }

            switch (request.RequestType)
            {
                case ModerationRequestType.AddChapter:
                    if (proposedChapterData != null)
                    {
                        chapterContentForPreview = proposedChapterData.Content ?? "[Контент отсутствует в запросе]";
                        chapterNumber = proposedChapterData.Number ?? "N/A";
                        chapterTitle = proposedChapterData.Title ?? "N/A";
                    }
                    else
                    {
                        chapterContentForPreview = "[Ошибка: не удалось загрузить данные предлагаемой главы]";
                    }
                    break;

                case ModerationRequestType.EditChapter:
                    if (proposedChapterData != null)
                    {
                        chapterContentForPreview = proposedChapterData.Content ?? "[Контент отсутствует в предлагаемых изменениях]";
                        chapterNumber = proposedChapterData.Number ?? "N/A";
                        chapterTitle = proposedChapterData.Title ?? "N/A";
                    }
                    else
                    {
                        chapterContentForPreview = "[Ошибка: не удалось загрузить предлагаемые изменения]";
                    }
                    // If we need to show original chapter number/title if not in proposed, fetch existing:
                    if (request.ChapterId.HasValue && (string.IsNullOrEmpty(proposedChapterData?.Number) || string.IsNullOrEmpty(proposedChapterData?.Title)))
                    {
                        existingChapterData = await _mySqlService.GetChapterAsync(request.ChapterId.Value);
                        if (existingChapterData != null)
                        {
                            if (string.IsNullOrEmpty(proposedChapterData?.Number)) chapterNumber = existingChapterData.Number ?? chapterNumber;
                            if (string.IsNullOrEmpty(proposedChapterData?.Title)) chapterTitle = existingChapterData.Title ?? chapterTitle;
                        }
                    }
                    break;

                case ModerationRequestType.DeleteChapter:
                    if (request.ChapterId.HasValue)
                    {
                        existingChapterData = await _mySqlService.GetChapterAsync(request.ChapterId.Value);
                        if (existingChapterData != null)
                        {
                            // Content for DeleteChapter is the existing chapter's content
                            chapterContentForPreview = existingChapterData.Content ?? "[Контент удаляемой главы отсутствует или не может быть загружен]";
                            chapterNumber = existingChapterData.Number ?? "N/A";
                            chapterTitle = existingChapterData.Title ?? "N/A";
                        }
                        else
                        {
                            chapterContentForPreview = "[Ошибка: не удалось загрузить данные удаляемой главы]";
                        }
                    }
                    else
                    {
                        chapterContentForPreview = "[Ошибка: ID главы для удаления не указан в запросе]";
                    }
                    break;

                default:
                    _logger.LogWarning("ChapterRequestPreview: Unsupported RequestType {RequestType} for request ID {RequestId}", request.RequestType, requestId);
                    return BadRequest("Неподдерживаемый тип запроса для предпросмотра главы.");
            }

            // Basic rendering of [img:...] tags, can be expanded if needed
            string renderedContentHtml = Regex.Replace(chapterContentForPreview, @"\[img:([^\]]+)\]",
                match => $"<img src=\"{match.Groups[1].Value.Trim()}\" alt=\"изображение из главы\" style=\"max-width:100%; display:block; margin:10px auto;\">");


            var previewViewModel = new ChapterPreviewViewModel
            {
                NovelTitle = parentNovel.Title,
                ChapterFullTitle = $"{parentNovel.Title}. {chapterNumber}{(string.IsNullOrWhiteSpace(chapterTitle) || chapterTitle == "N/A" ? "" : " - " + chapterTitle)}",
                RenderedContent = renderedContentHtml
            };

            return View("~/Views/Admin/ChapterRequestPreview.cshtml", previewViewModel);
        }

        // This method might still be used by Approve/Reject logic if it needs to reconstruct paths for deletion
        // for chapters that might not have had ContentFilePath populated during moderation request creation (older data).
        // However, with ContentFilePath now part of Chapter model and MySqlService, direct use of stored paths is preferred.
        // For ApproveChapterRequest, the ContentFilePath from existingChapter (for Edit/Delete) or newly saved (for Add) should be used.
        // For RejectChapterRequest, no file operations are typically needed beyond what FileService might do for temp files if any.
        private string ReconstructChapterFilePath(int novelId, string chapterNumber, string chapterTitle)
        {
            string invalidCharsRegex = string.Format(@"([{0}]*\.+$)|([{0}]+)", Regex.Escape(new string(Path.GetInvalidFileNameChars())));
            string numberPart = string.IsNullOrWhiteSpace(chapterNumber) ? "Chapter" : Regex.Replace(chapterNumber, invalidCharsRegex, "_");
            string titlePart = string.IsNullOrWhiteSpace(chapterTitle) ? "Untitled" : Regex.Replace(chapterTitle, invalidCharsRegex, "_");
            const int maxPartLength = 50;
            numberPart = numberPart.Length > maxPartLength ? numberPart.Substring(0, maxPartLength).Trim() : numberPart.Trim();
            titlePart = titlePart.Length > maxPartLength ? titlePart.Substring(0, maxPartLength).Trim() : titlePart.Trim();
            string finalFileName;
            bool numIsEmpty = string.IsNullOrWhiteSpace(chapterNumber) || numberPart == "_" || numberPart.Equals("Chapter", StringComparison.OrdinalIgnoreCase);
            bool titleIsEmpty = string.IsNullOrWhiteSpace(chapterTitle) || titlePart == "_" || titlePart.Equals("Untitled", StringComparison.OrdinalIgnoreCase);
            if (numIsEmpty && titleIsEmpty) finalFileName = "Глава без номера и названия.txt";
            else if (titleIsEmpty) finalFileName = $"{numberPart}.txt";
            else if (numIsEmpty) finalFileName = $"{titlePart}.txt";
            else finalFileName = $"{numberPart} - {titlePart}.txt";
            finalFileName = Regex.Replace(finalFileName, invalidCharsRegex, "_").Trim().Trim(new[] { '_', '.' });
            if (string.IsNullOrWhiteSpace(finalFileName) || finalFileName.Equals(".txt", StringComparison.OrdinalIgnoreCase)) finalFileName = "DefaultChapterName.txt";
            else if (!finalFileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) finalFileName += ".txt";
            return $"/uploads/content/{novelId}/{finalFileName}";
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveChapterRequest(int requestId)
        {
            var currentAdminUser = _currentUserService.GetCurrentUser();
            if (currentAdminUser == null || !_permissionService.CanModerateChapterRequests(currentAdminUser))
            {
                return Json(new { success = false, message = "Недостаточно прав." });
            }

            ModerationRequest request = await _mySqlService.GetModerationRequestByIdAsync(requestId);
            if (request == null)
            {
                return Json(new { success = false, message = "Запрос не найден." });
            }
            if (request.Status != ModerationStatus.Pending)
            {
                return Json(new { success = false, message = "Запрос уже обработан." });
            }

            try
            {
                switch (request.RequestType)
                {
                    case ModerationRequestType.AddChapter:
                        var chapterData = JsonSerializer.Deserialize<Chapter>(request.RequestData, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (chapterData == null) throw new JsonException("Не удалось десериализовать данные для AddChapter.");
                        if (!request.NovelId.HasValue) throw new InvalidOperationException("NovelId отсутствует в запросе на добавление главы.");

                        string filePath = await _fileService.SaveChapterContentAsync(request.NovelId.Value, chapterData.Number, chapterData.Title, chapterData.Content);
                        if (string.IsNullOrEmpty(filePath)) throw new Exception("Ошибка сохранения файла главы.");

                        chapterData.NovelId = request.NovelId.Value;
                        chapterData.ContentFilePath = filePath;
                        chapterData.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        chapterData.CreatorId = request.UserId;
                        await _mySqlService.CreateChapterAsync(chapterData); // chapterData.Id will be updated by this call
                        await _mySqlService.AddNovelTranslatorIfNotExistsAsync(request.NovelId.Value, request.UserId);

                        // Notification for novel subscribers
                        var novelForAdd = _mySqlService.GetNovel(request.NovelId.Value);
                        // Use string statuses as stored in the database and expected by GetUserIdsSubscribedToNovel
                        var statusesForNotification = new List<string> { "reading", "read", "favorites" }; // "Читаю", "Прочитало", "Любимое"
                        var subscribersForAdd = _mySqlService.GetUserIdsSubscribedToNovel(request.NovelId.Value, statusesForNotification);
                        var newChapterMsg = $"Новая глава '{chapterData.Number} - {chapterData.Title}' добавлена к новелле '{novelForAdd?.Title ?? "N/A"}'.";
                        foreach (var subId in subscribersForAdd)
                        {
                            // Do not notify the user who submitted the request if they are also a subscriber
                            if (subId != request.UserId)
                            {
                                await _notificationService.CreateNotification(subId, NotificationType.NewChapter, newChapterMsg, chapterData.Id, RelatedItemType.Chapter);
                            }
                        }
                        // Notification for chapter author (request submitter) is handled by the generic "RequestApproved" notification later.
                        break;

                    case ModerationRequestType.EditChapter:
                        var proposedChanges = JsonSerializer.Deserialize<Chapter>(request.RequestData, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (proposedChanges == null) throw new JsonException("Не удалось десериализовать данные для EditChapter.");
                        if (!request.ChapterId.HasValue) throw new InvalidOperationException("ChapterId отсутствует в запросе на редактирование главы.");

                        Chapter existingChapter = await _mySqlService.GetChapterAsync(request.ChapterId.Value);
                        if (existingChapter == null) throw new Exception("Редактируемая глава не найдена.");

                        string oldFilePath = existingChapter.ContentFilePath;
                        string newFilePath = await _fileService.SaveChapterContentAsync(existingChapter.NovelId, proposedChanges.Number, proposedChanges.Title, proposedChanges.Content);
                        if (string.IsNullOrEmpty(newFilePath)) throw new Exception("Ошибка сохранения файла обновленной главы.");

                        existingChapter.Number = proposedChanges.Number ?? existingChapter.Number;
                        existingChapter.Title = proposedChanges.Title ?? existingChapter.Title;
                        existingChapter.ContentFilePath = newFilePath;
                        // existingChapter.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); // Optionally update date on edit
                        await _mySqlService.UpdateChapterAsync(existingChapter);

                        if (!string.IsNullOrEmpty(oldFilePath) && oldFilePath != newFilePath)
                        {
                            await _fileService.DeleteChapterContentAsync(oldFilePath);
                        }
                        break;

                    case ModerationRequestType.DeleteChapter:
                        if (!request.ChapterId.HasValue) throw new InvalidOperationException("ChapterId отсутствует в запросе на удаление главы.");

                        Chapter chapterToDelete = await _mySqlService.GetChapterAsync(request.ChapterId.Value);
                        if (chapterToDelete != null)
                        {
                            if (!string.IsNullOrEmpty(chapterToDelete.ContentFilePath))
                            {
                                await _fileService.DeleteChapterContentAsync(chapterToDelete.ContentFilePath);
                            }
                            await _mySqlService.DeleteChapterAsync(request.ChapterId.Value);
                            if (chapterToDelete.CreatorId.HasValue && request.NovelId.HasValue)
                            {
                                await _mySqlService.RemoveTranslatorIfLastChapterAsync(request.NovelId.Value, chapterToDelete.CreatorId.Value);
                            }
                        }
                        else { _logger.LogWarning("Глава {ChapterId} для удаления не найдена.", request.ChapterId.Value); }
                        break;
                    default:
                        throw new InvalidOperationException("Неподдерживаемый тип запроса на модерацию главы.");
                }

                await _mySqlService.UpdateModerationRequestStatusAsync(requestId, ModerationStatus.Approved, currentAdminUser.Id);

                // Generic notification for the user who made the request
                var novelForNotification = request.NovelId.HasValue ? _mySqlService.GetNovel(request.NovelId.Value) : null;
                var chapterForNotification = request.ChapterId.HasValue ? await _mySqlService.GetChapterAsync(request.ChapterId.Value) : null;

                // Try to get chapter title from existing chapter if available, otherwise from request data (for AddChapter)
                string itemTitle = chapterForNotification?.Title;
                if (string.IsNullOrEmpty(itemTitle) && request.RequestType == ModerationRequestType.AddChapter && !string.IsNullOrEmpty(request.RequestData))
                {
                    try
                    {
                        var chapterDetails = JsonSerializer.Deserialize<Chapter>(request.RequestData, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        itemTitle = chapterDetails?.Title;
                    }
                    catch (JsonException) { /* ignore, fallback to N/A */ }
                }
                itemTitle = itemTitle ?? "N/A";

                string novelTitleForNotification = novelForNotification?.Title ?? "N/A";
                string actionDisplayName = GetActionDisplayName(request.RequestType);
                // Pass null for reason when request is approved.
                await _notificationService.CreateNotification(request.UserId, NotificationType.RequestApproved, $"Запрос на {actionDisplayName} главы '{itemTitle}' (новелла '{novelTitleForNotification}') одобрен.", request.ChapterId ?? request.NovelId, RelatedItemType.Chapter, null);

                return Json(new { success = true, message = "Запрос одобрен." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при одобрении запроса ID {RequestId} на модерацию главы.", requestId);
                return Json(new { success = false, message = $"Внутренняя ошибка сервера: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectChapterRequest(int requestId, string reason) // 'reason' from form
        {
            var currentAdminUser = _currentUserService.GetCurrentUser();
            if (currentAdminUser == null || !_permissionService.CanModerateChapterRequests(currentAdminUser))
            {
                return Json(new { success = false, message = "Недостаточно прав." });
            }

            ModerationRequest request = await _mySqlService.GetModerationRequestByIdAsync(requestId);
            if (request == null)
            {
                return Json(new { success = false, message = "Запрос не найден." });
            }
            if (request.Status != ModerationStatus.Pending)
            {
                return Json(new { success = false, message = "Запрос уже обработан." });
            }

            try
            {
                // The 'reason' parameter from the form is now 'moderationComment' for consistency with model
                await _mySqlService.UpdateModerationRequestStatusAsync(requestId, ModerationStatus.Rejected, currentAdminUser.Id, reason);

                // Notification
                var novelForNotification = request.NovelId.HasValue ? _mySqlService.GetNovel(request.NovelId.Value) : null;
                var chapterForNotification = request.ChapterId.HasValue ? await _mySqlService.GetChapterAsync(request.ChapterId.Value) : null;

                string itemTitle = chapterForNotification?.Title;
                if (string.IsNullOrEmpty(itemTitle) && (request.RequestType == ModerationRequestType.AddChapter || request.RequestType == ModerationRequestType.EditChapter) && !string.IsNullOrEmpty(request.RequestData))
                {
                    try
                    {
                        var chapterDetails = JsonSerializer.Deserialize<Chapter>(request.RequestData, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        itemTitle = chapterDetails?.Title;
                    }
                    catch (JsonException) { /* ignore, fallback to N/A */ }
                }
                itemTitle = itemTitle ?? "N/A";

                string novelTitleForNotification = novelForNotification?.Title ?? "N/A";
                string actionDisplayName = GetActionDisplayName(request.RequestType);
                string notificationMsg = $"Запрос на {actionDisplayName} главы '{itemTitle}' (новелла '{novelTitleForNotification}') отклонен.";
                // Pass the rejection reason to the notification service.
                await _notificationService.CreateNotification(request.UserId, NotificationType.RequestRejected, notificationMsg, request.Id, RelatedItemType.ModerationRequest, reason);

                return Json(new { success = true, message = "Запрос отклонен." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отклонении запроса ID {RequestId} на модерацию главы.", requestId);
                return Json(new { success = false, message = $"Внутренняя ошибка сервера: {ex.Message}" });
            }
        }

        // The HandleProcessChapterRequest method is now replaced by specific ApproveChapterRequest and RejectChapterRequest.
        // private async Task<IActionResult> HandleProcessChapterRequest(int requestId, bool approve, string moderationComment)
        // {
        // ... (old combined logic removed)
        // }

        public async Task<IActionResult> NovelRequestPreview(int requestId)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateNovelRequests(currentUser)) // Assuming same permission
            {
                // Or redirect to a generic access denied page if not returning a partial
                return RedirectToAction("AccessDenied", "AuthView");
            }

            ModerationRequest request = _mySqlService.GetModerationRequestById(requestId);
            if (request == null)
            {
                return NotFound("Запрос на модерацию не найден.");
            }

            var requester = _mySqlService.GetUser(request.UserId);
            var viewModel = new NovelRequestPreviewViewModel
            {
                RequestId = request.Id,
                RequestType = request.RequestType,
                RequesterLogin = requester?.Login ?? "N/A",
                // RequestTypeDisplayName will be set by GetFriendlyRequestTypeName in VM
            };

            string authorLogin = "Не указан";

            try
            {
                switch (request.RequestType)
                {
                    case ModerationRequestType.AddNovel:
                        var addData = JsonSerializer.Deserialize<Novel>(request.RequestData, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (addData != null)
                        {
                            viewModel.Title = addData.Title;
                            viewModel.Description = addData.Description;
                            viewModel.CoversList = NovelRequestPreviewViewModel.ParseJsonStringToList(addData.Covers);
                            viewModel.GenresList = NovelRequestPreviewViewModel.ParseJsonStringToList(addData.Genres);
                            viewModel.TagsList = NovelRequestPreviewViewModel.ParseJsonStringToList(addData.Tags);
                            viewModel.Type = addData.Type;
                            viewModel.Format = addData.Format;
                            viewModel.ReleaseYear = addData.ReleaseYear;
                            viewModel.AuthorId = addData.AuthorId;
                            viewModel.AlternativeTitles = addData.AlternativeTitles;
                            viewModel.RelatedNovelIds = addData.RelatedNovelIds; // Assuming string
                            viewModel.Date = addData.Date;
                            viewModel.Status = addData.Status; // NovelStatus enum
                        }
                        break;

                    case ModerationRequestType.EditNovel:
                        var editPayload = JsonSerializer.Deserialize<NovelEditModerationData>(request.RequestData, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (editPayload != null && editPayload.UpdatedFields != null && request.NovelId.HasValue)
                        {
                            Novel existingNovel = _mySqlService.GetNovel(request.NovelId.Value);
                            if (existingNovel == null) return NotFound("Редактируемая новелла не найдена.");

                            viewModel.Id = existingNovel.Id;
                            viewModel.OriginalNovelTitle = existingNovel.Title; // Store original title

                            // Populate with existing data first
                            viewModel.Title = existingNovel.Title;
                            viewModel.Description = existingNovel.Description;
                            viewModel.CoversList = NovelRequestPreviewViewModel.ParseJsonStringToList(existingNovel.Covers);
                            viewModel.GenresList = NovelRequestPreviewViewModel.ParseJsonStringToList(existingNovel.Genres);
                            viewModel.TagsList = NovelRequestPreviewViewModel.ParseJsonStringToList(existingNovel.Tags);
                            viewModel.Type = existingNovel.Type;
                            viewModel.Format = existingNovel.Format;
                            viewModel.ReleaseYear = existingNovel.ReleaseYear;
                            viewModel.AuthorId = existingNovel.AuthorId;
                            viewModel.AlternativeTitles = existingNovel.AlternativeTitles;
                            viewModel.RelatedNovelIds = existingNovel.RelatedNovelIds;
                            viewModel.Date = existingNovel.Date;
                            viewModel.Status = existingNovel.Status;

                            // Apply updated fields
                            var updated = editPayload.UpdatedFields;
                            if (updated.Title != null) viewModel.Title = updated.Title;
                            if (updated.Description != null) viewModel.Description = updated.Description;
                            if (updated.Genres != null) viewModel.GenresList = NovelRequestPreviewViewModel.ParseJsonStringToList(updated.Genres);
                            if (updated.Tags != null) viewModel.TagsList = NovelRequestPreviewViewModel.ParseJsonStringToList(updated.Tags);
                            if (updated.Type != null) viewModel.Type = updated.Type;
                            if (updated.Format != null) viewModel.Format = updated.Format;
                            if (updated.ReleaseYear.HasValue) viewModel.ReleaseYear = updated.ReleaseYear;
                            if (updated.AuthorId.HasValue) viewModel.AuthorId = updated.AuthorId; // Can be null to unset author
                            else if (editPayload.UpdatedFields.GetType().GetProperty("AuthorId") != null && updated.AuthorId == null) viewModel.AuthorId = null; // Explicitly set to null if provided as null

                            if (updated.AlternativeTitles != null) viewModel.AlternativeTitles = updated.AlternativeTitles;
                            if (updated.RelatedNovelIds != null) viewModel.RelatedNovelIds = updated.RelatedNovelIds;

                            // Combine covers
                            var finalCovers = new List<string>();
                            if (editPayload.KeptExistingCovers != null) finalCovers.AddRange(editPayload.KeptExistingCovers);
                            if (editPayload.NewTempCoverPaths != null) finalCovers.AddRange(editPayload.NewTempCoverPaths);
                            viewModel.CoversList = finalCovers.Distinct().ToList();
                        }
                        else
                        {
                            _logger.LogWarning("EditNovel request ID {RequestId} has invalid data or missing NovelId.", requestId);
                            return BadRequest("Некорректные данные для предпросмотра редактирования.");
                        }
                        break;

                    case ModerationRequestType.DeleteNovel:
                        var deleteData = JsonSerializer.Deserialize<NovelDeleteModerationData>(request.RequestData, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        int novelIdToDelete = deleteData?.Id ?? request.NovelId ?? 0;

                        if (novelIdToDelete > 0)
                        {
                            Novel novelToDelete = _mySqlService.GetNovel(novelIdToDelete);
                            if (novelToDelete != null)
                            {
                                viewModel.Id = novelToDelete.Id;
                                viewModel.Title = novelToDelete.Title; // Title from DB, RequestData might only have ID
                                viewModel.OriginalNovelTitle = novelToDelete.Title; // For consistency
                                viewModel.Description = novelToDelete.Description;
                                viewModel.CoversList = NovelRequestPreviewViewModel.ParseJsonStringToList(novelToDelete.Covers);
                                viewModel.GenresList = NovelRequestPreviewViewModel.ParseJsonStringToList(novelToDelete.Genres);
                                viewModel.TagsList = NovelRequestPreviewViewModel.ParseJsonStringToList(novelToDelete.Tags);
                                viewModel.Type = novelToDelete.Type;
                                viewModel.Format = novelToDelete.Format;
                                viewModel.ReleaseYear = novelToDelete.ReleaseYear;
                                viewModel.AuthorId = novelToDelete.AuthorId;
                                viewModel.AlternativeTitles = novelToDelete.AlternativeTitles;
                                viewModel.RelatedNovelIds = novelToDelete.RelatedNovelIds;
                                viewModel.Date = novelToDelete.Date;
                                viewModel.Status = novelToDelete.Status;
                                viewModel.IsPendingDeletion = true;
                            }
                            else return NotFound("Новелла для удаления не найдена.");
                        }
                        else
                        {
                            _logger.LogWarning("DeleteNovel request ID {RequestId} has invalid data or missing NovelId.", requestId);
                            return BadRequest("Некорректные данные для предпросмотра удаления.");
                        }
                        break;

                    default:
                        return BadRequest("Неподдерживаемый тип запроса для предпросмотра.");
                }

                if (viewModel.AuthorId.HasValue && viewModel.AuthorId > 0)
                {
                    var author = _mySqlService.GetUser(viewModel.AuthorId.Value);
                    if (author != null)
                    {
                        authorLogin = author.Login;
                    }
                }
                viewModel.AuthorLogin = authorLogin;


            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "Error deserializing RequestData for preview, RequestId: {RequestId}", requestId);
                // Consider returning a specific error view or message
                return View("~/Views/Shared/Error.cshtml", new ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier, Message = "Ошибка при чтении данных запроса для предпросмотра." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generic error during NovelRequestPreview for RequestId: {RequestId}", requestId);
                return View("~/Views/Shared/Error.cshtml", new ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier, Message = "Произошла ошибка при подготовке предпросмотра." });
            }

            viewModel.RequestTypeDisplayName = viewModel.GetFriendlyRequestTypeName();
            return View("~/Views/Admin/NovelRequestPreview.cshtml", viewModel);
        }
    }
}
