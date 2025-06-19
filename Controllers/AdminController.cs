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

        public IActionResult Index()
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanViewAdminPanel(currentUser))
            { return RedirectToAction("AccessDenied", "AuthView"); }
            return View("~/Views/Admin/Index.cshtml");
        }

        public IActionResult UsersPartial()
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanManageUsers(currentUser))
            { return PartialView("~/Views/Shared/_AccessDeniedPartial.cshtml"); }
            List<User> users = _mySqlService.GetAllUsers();
            return PartialView("~/Views/Admin/_UsersPartial.cshtml", users);
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
                            novelForNotification = createData; createData.AuthorId = request.UserId; createData.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); createData.Status = NovelStatus.Approved;
                            var dbNovel = new Novel { Title = createData.Title, Description = createData.Description, AuthorId = createData.AuthorId, Genres = createData.Genres, Tags = createData.Tags, Type = createData.Type, Format = createData.Format, ReleaseYear = createData.ReleaseYear, AlternativeTitles = createData.AlternativeTitles, RelatedNovelIds = createData.RelatedNovelIds, Date = createData.Date, Status = createData.Status, Covers = "[]" };
                            int newId = _mySqlService.CreateNovel(dbNovel);
                            List<string> tempP = new List<string>(); if (!string.IsNullOrEmpty(createData.Covers)) { try { tempP = JsonSerializer.Deserialize<List<string>>(createData.Covers); } catch { } }
                            List<string> finalP = new List<string>(); if (tempP != null) { foreach (var p in tempP) { if (!string.IsNullOrEmpty(p) && p.Contains("/temp_covers/")) { string fp = await _fileService.CommitTempCoverAsync(p, newId); if (!string.IsNullOrEmpty(fp)) finalP.Add(fp); } else if (!string.IsNullOrEmpty(p)) { finalP.Add(p); } } }
                            if (finalP.Any()) { var utN = _mySqlService.GetNovel(newId); if (utN != null) { utN.Covers = JsonSerializer.Serialize(finalP); _mySqlService.UpdateNovel(utN); } }
                            request.NovelId = newId; break;
                        case ModerationRequestType.EditNovel:
                            var updateData = JsonSerializer.Deserialize<NovelUpdateRequest>(request.RequestData); if (updateData == null) throw new JsonException("Null data EditNovel");
                            var novelTU = _mySqlService.GetNovel(request.NovelId.Value); if (novelTU == null) throw new Exception("Novel to update not found");
                            originalNovelTitleForNotification = novelTU.Title; novelForNotification = novelTU;
                            novelTU.Title = updateData.Title ?? novelTU.Title; novelTU.Description = updateData.Description ?? novelTU.Description; novelTU.Genres = updateData.Genres ?? novelTU.Genres; novelTU.Tags = updateData.Tags ?? novelTU.Tags; novelTU.Type = updateData.Type ?? novelTU.Type; novelTU.Format = updateData.Format ?? novelTU.Format; novelTU.ReleaseYear = updateData.ReleaseYear ?? novelTU.ReleaseYear; novelTU.AuthorId = updateData.AuthorId ?? novelTU.AuthorId; novelTU.AlternativeTitles = updateData.AlternativeTitles ?? novelTU.AlternativeTitles;
                            List<string> pathsFromReq = updateData.Covers ?? new List<string>(); List<string> finalUpdP = new List<string>(); List<string> tempToCommit = new List<string>();
                            foreach (var pIR in pathsFromReq) { if (!string.IsNullOrEmpty(pIR)) { if (pIR.Contains("/temp_covers/")) tempToCommit.Add(pIR); else finalUpdP.Add(pIR); } }
                            foreach (var tP in tempToCommit) { string fP = await _fileService.CommitTempCoverAsync(tP, novelTU.Id); if (!string.IsNullOrEmpty(fP)) finalUpdP.Add(fP); }
                            List<string> existingCDB = new List<string>(); if (!string.IsNullOrWhiteSpace(novelTU.Covers)) { try { existingCDB = JsonSerializer.Deserialize<List<string>>(novelTU.Covers); } catch { } }
                            foreach (var oCP in existingCDB) { if (!finalUpdP.Contains(oCP) && !tempToCommit.Contains(oCP)) { await _fileService.DeleteCoverAsync(oCP); } }
                            novelTU.Covers = JsonSerializer.Serialize(finalUpdP.Distinct().ToList()); _mySqlService.UpdateNovel(novelTU); break;
                        case ModerationRequestType.DeleteNovel:
                            if (!request.NovelId.HasValue) throw new Exception("NovelId missing"); var novelTD = _mySqlService.GetNovel(request.NovelId.Value);
                            if (novelTD != null) { novelForNotification = new Novel { Title = novelTD.Title }; if (!string.IsNullOrWhiteSpace(novelTD.Covers)) { var cTD = JsonSerializer.Deserialize<List<string>>(novelTD.Covers); if (cTD != null) { foreach (var cP in cTD) await _fileService.DeleteCoverAsync(cP); } } _mySqlService.DeleteNovel(request.NovelId.Value); }
                            else { _logger.LogWarning("Novel {Nid} for del not found", request.NovelId.Value); }
                            break;
                        default: throw new InvalidOperationException("Unsupported type");
                    }
                    _mySqlService.UpdateModerationRequest(request);
                    var finalNT = novelForNotification?.Title ?? originalNovelTitleForNotification ?? "?";
                    _notificationService.CreateNotification(request.UserId, NotificationType.ModerationApproved, $"Запрос '{request.RequestType}' для новеллы '{finalNT}' одобрен.", request.NovelId, RelatedItemType.Novel);
                    return Json(new { success = true, message = $"Запрос ID {requestId} ({request.RequestType}) одобрен." });
                }
                catch (Exception ex) { _logger.LogError(ex, "Error approving req ID {Rid}", requestId); return Json(new { success = false, message = "Ошибка одобрения запроса" }); }
            }
            else
            {
                request.Status = ModerationStatus.Rejected; _mySqlService.UpdateModerationRequest(request);
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
                string rejNT = "?"; if (request.RequestType == ModerationRequestType.AddNovel && !string.IsNullOrEmpty(request.RequestData)) { try { var nt = JsonSerializer.Deserialize<Novel>(request.RequestData); rejNT = nt?.Title ?? rejNT; } catch { } }
                else if (request.NovelId.HasValue) { rejNT = _mySqlService.GetNovel(request.NovelId.Value)?.Title ?? rejNT; }
                else if (request.RequestType == ModerationRequestType.DeleteNovel && !string.IsNullOrEmpty(request.RequestData)) { try { rejNT = JsonDocument.Parse(request.RequestData).RootElement.GetProperty("Title").GetString() ?? rejNT; } catch { } }
                var rejMsg = $"Запрос '{request.RequestType}' для новеллы '{rejNT}' отклонен."; if (!string.IsNullOrWhiteSpace(moderationComment)) rejMsg += $" Причина: {moderationComment}";
                _notificationService.CreateNotification(request.UserId, NotificationType.ModerationRejected, rejMsg, request.NovelId, RelatedItemType.Novel);
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
                viewModel.ExistingChapterData = _mySqlService.GetChapter(request.ChapterId.Value);
                if (viewModel.ExistingChapterData != null)
                {
                    viewModel.ChapterNumber = viewModel.ExistingChapterData.Number; viewModel.ChapterTitle = viewModel.ExistingChapterData.Title;
                    string path = ReconstructChapterFilePath(viewModel.ExistingChapterData.NovelId, viewModel.ExistingChapterData.Number, viewModel.ExistingChapterData.Title);
                    viewModel.ExistingContent = await _fileService.ReadChapterContentAsync(path) ?? "[Не удалось загрузить существующий контент]";
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
                        // Notification for chapter author handled below
                        // Notification for novel subscribers
                        var novelForAdd = _mySqlService.GetNovel(request.NovelId.Value); // Sync call, consider async if performance is an issue
                        var subscribersForAdd = _mySqlService.GetUserIdsSubscribedToNovel(request.NovelId.Value, new List<string> { "reading", "favorites" });
                        var newChapterMsg = $"Новая глава '{chapterData.Number} - {chapterData.Title}' добавлена к новелле '{novelForAdd?.Title ?? "N/A"}'.";
                        foreach (var subId in subscribersForAdd) { if (subId != request.UserId) { _notificationService.CreateNotification(subId, NotificationType.NewChapter, newChapterMsg, chapterData.Id, RelatedItemType.Chapter); } }
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
                string itemTitle = chapterForNotification?.Title ?? (JsonSerializer.Deserialize<Chapter>(request.RequestData, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Title) ?? "N/A";
                string novelTitleForNotification = novelForNotification?.Title ?? "N/A";
                _notificationService.CreateNotification(request.UserId, NotificationType.ModerationApproved, $"Ваш запрос '{request.RequestType.ToString()}' для главы '{itemTitle}' (новелла '{novelTitleForNotification}') одобрен.", request.ChapterId ?? request.NovelId, RelatedItemType.Chapter);

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
                string itemTitle = chapterForNotification?.Title ?? (JsonSerializer.Deserialize<Chapter>(request.RequestData, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Title) ?? "N/A";
                string novelTitleForNotification = novelForNotification?.Title ?? "N/A";
                string notificationMsg = $"Ваш запрос '{request.RequestType.ToString()}' для главы '{itemTitle}' (новелла '{novelTitleForNotification}') отклонен.";
                if (!string.IsNullOrWhiteSpace(reason)) notificationMsg += $" Причина: {reason}";
                _notificationService.CreateNotification(request.UserId, NotificationType.ModerationRejected, notificationMsg, request.ChapterId ?? request.NovelId, RelatedItemType.Chapter);


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
    }
}
