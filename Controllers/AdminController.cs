using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;
using BulbaLib.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic; // For List

namespace BulbaLib.Controllers
{
    [Authorize(Roles = "Admin")] // Require Admin role for all actions in this controller
    public class AdminController : Controller
    {
        private readonly MySqlService _mySqlService;
        private readonly PermissionService _permissionService;
        private readonly ICurrentUserService _currentUserService;
        private readonly INotificationService _notificationService; // Added

        public AdminController(
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
        public IActionResult Users()
        {
            var currentUser = _currentUserService.GetCurrentUser();
            // This check might be redundant if Index() already verified CanViewAdminPanel,
            // but good for direct access or if Users() is called independently.
            if (currentUser == null || !_permissionService.CanManageUsers(currentUser))
            {
                // For AJAX, return a partial view indicating error or an empty content
                return PartialView("~/Views/Shared/_AccessDeniedPartial.cshtml");
            }

            List<User> users = _mySqlService.GetAllUsers();
            return PartialView("~/Views/Admin/_UsersPartial.cshtml", users);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChangeUserRole(int userId, string role)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            User targetUser = _mySqlService.GetUser(userId);

            if (currentUser == null || targetUser == null || !_permissionService.CanChangeUserRole(currentUser, targetUser))
            {
                return Json(new { success = false, message = "Недостаточно прав для изменения роли." });
            }

            var validRoles = new List<string> { "User", "Admin", "Author", "Translator" };
            if (!validRoles.Contains(role))
            {
                return Json(new { success = false, message = "Недопустимая роль." });
            }

            _mySqlService.UpdateUserRole(userId, role);
            // TempData not suitable for AJAX. Return JSON.
            return Json(new { success = true, message = $"Роль пользователя {targetUser.Login} изменена на {role}." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleUserBlock(int userId)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            User targetUser = _mySqlService.GetUser(userId);

            if (currentUser == null || targetUser == null || !_permissionService.CanBlockUser(currentUser, targetUser))
            {
                return Json(new { success = false, message = "Недостаточно прав для блокировки/разблокировки пользователя." });
            }

            bool newBlockStatus = !targetUser.IsBlocked;
            _mySqlService.SetUserBlockedStatus(userId, newBlockStatus);
            return Json(new { success = true, message = $"Пользователь {targetUser.Login} {(newBlockStatus ? "заблокирован" : "разблокирован")}.", isBlocked = newBlockStatus });
        }

        // Novel Moderation Requests
        public IActionResult NovelRequests(int page = 1)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateNovelRequests(currentUser))
            {
                return PartialView("~/Views/Shared/_AccessDeniedPartial.cshtml");
            }

            int pageSize = 10;
            var novelRequestTypes = new List<ModerationRequestType>
                { ModerationRequestType.AddNovel, ModerationRequestType.EditNovel, ModerationRequestType.DeleteNovel };

            List<ModerationRequest> rawRequests = _mySqlService.GetPendingModerationRequestsByType(novelRequestTypes, pageSize, (page - 1) * pageSize);
            var viewModels = new List<NovelModerationRequestViewModel>();

            foreach (var req in rawRequests)
            {
                var vm = new NovelModerationRequestViewModel
                {
                    RequestId = req.Id,
                    RequestType = req.RequestType,
                    RequestTypeDisplay = req.RequestType.ToString(), // TODO: Add display names
                    UserId = req.UserId,
                    RequesterLogin = _mySqlService.GetUser(req.UserId)?.Login ?? "N/A",
                    CreatedAt = req.CreatedAt,
                    NovelId = req.NovelId,
                    RequestDataJson = req.RequestData,
                    Status = req.Status.ToString()
                };

                if (req.RequestType == ModerationRequestType.AddNovel && !string.IsNullOrEmpty(req.RequestData))
                {
                    try
                    {
                        vm.ProposedNovelData = System.Text.Json.JsonSerializer.Deserialize<Novel>(req.RequestData);
                        vm.NovelTitle = vm.ProposedNovelData?.Title;
                    }
                    catch { /* log error */ }
                }
                else if (req.NovelId.HasValue)
                {
                    vm.ExistingNovelData = _mySqlService.GetNovel(req.NovelId.Value);
                    vm.NovelTitle = vm.ExistingNovelData?.Title;
                    if (req.RequestType == ModerationRequestType.EditNovel && !string.IsNullOrEmpty(req.RequestData))
                    {
                        try { vm.ProposedNovelData = System.Text.Json.JsonSerializer.Deserialize<Novel>(req.RequestData); } catch { /* log error */ }
                        // TODO: Populate ChangeSummary for EditNovel if needed
                    }
                }
                viewModels.Add(vm);
            }

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

            Novel requestedNovelData = null;
            Novel existingNovel = null;

            if (request.RequestType == ModerationRequestType.AddNovel || request.RequestType == ModerationRequestType.EditNovel)
            {
                if (!string.IsNullOrEmpty(request.RequestData))
                {
                    try { requestedNovelData = System.Text.Json.JsonSerializer.Deserialize<Novel>(request.RequestData); }
                    catch { /* Handle error, maybe log it */ }
                }
            }
            if (request.NovelId.HasValue)
            {
                existingNovel = _mySqlService.GetNovel(request.NovelId.Value);
            }

            ViewData["RequestedNovel"] = requestedNovelData;
            ViewData["ExistingNovel"] = existingNovel;
            // Fetch user who made the request for display
            var requester = _mySqlService.GetUser(request.UserId);
            ViewData["RequesterUserLogin"] = requester?.Login ?? "Неизвестный пользователь";


            // This should return a PartialView if it's meant to be loaded in a modal or section of a page.
            // If it's a full page, View() is fine. Assuming full page for details for now.
            return View("~/Views/Admin/NovelRequestDetails.cshtml", request);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ApproveNovelRequest(int requestId)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateNovelRequests(currentUser))
            {
                // For AJAX, return JSON
                return Json(new { success = false, message = "Недостаточно прав." });
            }

            ModerationRequest request = _mySqlService.GetModerationRequestById(requestId);
            if (request == null || request.Status != ModerationStatus.Pending)
            {
                return Json(new { success = false, message = "Запрос не найден или уже обработан." });
            }

            Novel novelData = null;
            Novel existingNovel = null;

            try
            {
                switch (request.RequestType)
                {
                    case ModerationRequestType.AddNovel:
                        novelData = System.Text.Json.JsonSerializer.Deserialize<Novel>(request.RequestData);
                        // AuthorId should be from the user who made the request (request.UserId)
                        novelData.AuthorId = request.UserId;
                        novelData.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        novelData.Covers = string.IsNullOrWhiteSpace(novelData.Covers) ? "[]" : novelData.Covers;
                        _mySqlService.CreateNovel(novelData);
                        break;
                    case ModerationRequestType.EditNovel:
                        novelData = System.Text.Json.JsonSerializer.Deserialize<Novel>(request.RequestData);
                        existingNovel = _mySqlService.GetNovel(request.NovelId.Value);
                        if (existingNovel == null) throw new Exception("Original novel not found for edit.");

                        // Apply changes from novelData to existingNovel
                        existingNovel.Title = novelData.Title;
                        existingNovel.Description = novelData.Description;
                        existingNovel.Covers = string.IsNullOrWhiteSpace(novelData.Covers) ? "[]" : novelData.Covers;
                        existingNovel.Genres = novelData.Genres;
                        existingNovel.Tags = novelData.Tags;
                        existingNovel.Type = novelData.Type;
                        existingNovel.Format = novelData.Format;
                        existingNovel.ReleaseYear = novelData.ReleaseYear;
                        existingNovel.AlternativeTitles = novelData.AlternativeTitles;
                        // Ensure AuthorId is not changed by edit request unless intended by Admin specific UI
                        // existingNovel.AuthorId = novelData.AuthorId; // Usually, AuthorId should not change on edit.
                        _mySqlService.UpdateNovel(existingNovel);
                        break;
                    case ModerationRequestType.DeleteNovel:
                        if (!request.NovelId.HasValue) throw new Exception("NovelId is missing for delete request.");
                        _mySqlService.DeleteNovel(request.NovelId.Value);
                        break;
                    default:
                        throw new InvalidOperationException("Неподдерживаемый тип запроса.");
                }

                request.Status = ModerationStatus.Approved;
                request.ModeratorId = currentUser.Id;
                request.UpdatedAt = DateTime.UtcNow;
                _mySqlService.UpdateModerationRequest(request);

                // Send notification
                var novelTitleForNotification = novelData?.Title ?? existingNovel?.Title ?? "Неизвестная новелла";
                var message = $"Ваш запрос '{request.RequestType}' для новеллы '{novelTitleForNotification}' был одобрен.";
                // Assuming request.NovelId is the ID of the novel itself after creation/approval
                // For AddNovel, novelData.Id might be 0 if not refetched, so use request.NovelId which should be populated by CreateNovel or be the existing ID
                int? relatedNovelId = (request.RequestType == ModerationRequestType.AddNovel)
                                      ? (_mySqlService.GetNovels().FirstOrDefault(n => n.Title == novelData.Title)?.Id) // Attempt to get new ID
                                      : request.NovelId;

                _notificationService.CreateNotification(request.UserId, NotificationType.ModerationApproved, message, relatedNovelId, RelatedItemType.Novel);

                return Json(new { success = true, message = $"Запрос ID {requestId} ({request.RequestType}) одобрен." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Ошибка обработки запроса ID {requestId}: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RejectNovelRequest(int requestId, string moderationComment)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateNovelRequests(currentUser))
            {
                return Json(new { success = false, message = "Недостаточно прав." });
            }

            ModerationRequest request = _mySqlService.GetModerationRequestById(requestId);
            if (request == null || request.Status != ModerationStatus.Pending)
            {
                return Json(new { success = false, message = "Запрос не найден или уже обработан." });
            }

            request.Status = ModerationStatus.Rejected;
            request.ModeratorId = currentUser.Id;
            request.ModerationComment = moderationComment;
            request.UpdatedAt = DateTime.UtcNow;
            _mySqlService.UpdateModerationRequest(request);

            // Send notification
            // Attempt to get novel title for notification message
            string novelTitleForNotification = "Неизвестная новелла";
            if (request.RequestType == ModerationRequestType.AddNovel && !string.IsNullOrEmpty(request.RequestData))
            {
                try { var nt = System.Text.Json.JsonSerializer.Deserialize<Novel>(request.RequestData); novelTitleForNotification = nt?.Title; } catch { }
            }
            else if (request.NovelId.HasValue)
            {
                novelTitleForNotification = _mySqlService.GetNovel(request.NovelId.Value)?.Title;
            }
            var message = $"Ваш запрос '{request.RequestType}' для новеллы '{novelTitleForNotification}' был отклонен.";
            if (!string.IsNullOrWhiteSpace(moderationComment)) message += $" Причина: {moderationComment}";
            _notificationService.CreateNotification(request.UserId, NotificationType.ModerationRejected, message, request.NovelId, RelatedItemType.Novel);

            return Json(new { success = true, message = $"Запрос ID {requestId} ({request.RequestType}) отклонен." });
        }

        // Chapter Moderation Requests
        public IActionResult ChapterRequests(int page = 1)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateChapterRequests(currentUser))
            {
                return PartialView("~/Views/Shared/_AccessDeniedPartial.cshtml");
            }

            int pageSize = 10;
            var chapterRequestTypes = new List<ModerationRequestType>
                { ModerationRequestType.AddChapter, ModerationRequestType.EditChapter, ModerationRequestType.DeleteChapter };

            List<ModerationRequest> requests = _mySqlService.GetPendingModerationRequestsByType(chapterRequestTypes, pageSize, (page - 1) * pageSize);
            int totalRequests = _mySqlService.CountPendingModerationRequestsByType(chapterRequestTypes);

            ViewData["TotalPages"] = (int)Math.Ceiling((double)totalRequests / pageSize);
            ViewData["CurrentPage"] = page;
            ViewData["PageSize"] = pageSize;

            // Map to ViewModel
            var viewModels = new List<ChapterModerationRequestViewModel>();
            foreach (var req in requests)
            {
                var novel = req.NovelId.HasValue ? _mySqlService.GetNovel(req.NovelId.Value) : null;
                Chapter proposedChapter = null;
                Chapter existingChapter = null;

                if (!string.IsNullOrEmpty(req.RequestData))
                {
                    try { proposedChapter = System.Text.Json.JsonSerializer.Deserialize<Chapter>(req.RequestData); } catch { /* log error */ }
                }
                if (req.ChapterId.HasValue)
                {
                    existingChapter = _mySqlService.GetChapter(req.ChapterId.Value);
                }

                viewModels.Add(new ChapterModerationRequestViewModel
                {
                    RequestId = req.Id,
                    RequestType = req.RequestType,
                    RequestTypeDisplay = req.RequestType.ToString(), // TODO: Add display names
                    UserId = req.UserId,
                    RequesterLogin = _mySqlService.GetUser(req.UserId)?.Login ?? "N/A",
                    CreatedAt = req.CreatedAt,
                    NovelId = req.NovelId,
                    NovelTitle = novel?.Title ?? "N/A",
                    ChapterId = req.ChapterId,
                    ChapterNumber = proposedChapter?.Number ?? existingChapter?.Number,
                    ChapterTitle = proposedChapter?.Title ?? existingChapter?.Title,
                    RequestDataJson = req.RequestData,
                    ProposedChapterData = proposedChapter,
                    ExistingChapterData = existingChapter,
                    Status = req.Status.ToString()
                });
            }
            return PartialView("~/Views/Admin/_ChapterRequestsPartial.cshtml", viewModels);
        }

        public IActionResult ChapterRequestDetails(int requestId)
        {
            // User currentUser = GetCurrentUser(); // This was from an older version, _currentUserService is used below
            var currentUser = _currentUserService.GetCurrentUser(); // Corrected to use injected service
            if (currentUser == null || !_permissionService.CanModerateChapterRequests(currentUser))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            ModerationRequest request = _mySqlService.GetModerationRequestById(requestId);
            if (request == null) return NotFound();

            Chapter requestedChapterData = null;
            Chapter existingChapter = null;
            Novel parentNovel = null;

            if (request.NovelId.HasValue)
            {
                parentNovel = _mySqlService.GetNovel(request.NovelId.Value);
            }

            if (request.RequestType == ModerationRequestType.AddChapter || request.RequestType == ModerationRequestType.EditChapter)
            {
                if (!string.IsNullOrEmpty(request.RequestData))
                {
                    try { requestedChapterData = System.Text.Json.JsonSerializer.Deserialize<Chapter>(request.RequestData); }
                    catch { /* Log error */ }
                }
            }
            if (request.ChapterId.HasValue)
            {
                existingChapter = _mySqlService.GetChapter(request.ChapterId.Value);
            }

            ViewData["RequestedChapter"] = requestedChapterData;
            ViewData["ExistingChapter"] = existingChapter;
            ViewData["ParentNovel"] = parentNovel;
            var requester = _mySqlService.GetUser(request.UserId); // Already using _mySqlService
            ViewData["RequesterUserLogin"] = requester?.Login ?? "Неизвестный пользователь";


            User moderatorUser = null;
            if (request.ModeratorId.HasValue)
            {
                moderatorUser = _mySqlService.GetUser(request.ModeratorId.Value);
            }
            ViewData["ModeratorUserLogin"] = moderatorUser?.Login;
            // Assuming full page for details for now.
            return View("~/Views/Admin/ChapterRequestDetails.cshtml", request);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ApproveChapterRequest(int requestId)
        {
            var currentUser = _currentUserService.GetCurrentUser(); // Corrected
            if (currentUser == null || !_permissionService.CanModerateChapterRequests(currentUser))
            {
                return Json(new { success = false, message = "Недостаточно прав." });
            }

            ModerationRequest request = _mySqlService.GetModerationRequestById(requestId);
            if (request == null || request.Status != ModerationStatus.Pending)
            {
                return Json(new { success = false, message = "Запрос не найден или уже обработан." });
            }

            Chapter chapterData = null;
            Chapter existingChapter = null;

            try
            {
                switch (request.RequestType)
                {
                    case ModerationRequestType.AddChapter:
                        chapterData = System.Text.Json.JsonSerializer.Deserialize<Chapter>(request.RequestData);
                        if (!request.NovelId.HasValue && chapterData.NovelId == 0) throw new Exception("NovelId for chapter is missing.");
                        // Ensure NovelId from request is used if chapterData.NovelId is not set (it should be)
                        chapterData.NovelId = request.NovelId ?? chapterData.NovelId;
                        chapterData.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        // TODO: Consider if chapterData needs a TranslatorId or AddedByUserId set from request.UserId
                        _mySqlService.CreateChapter(chapterData);
                        break;
                    case ModerationRequestType.EditChapter:
                        chapterData = System.Text.Json.JsonSerializer.Deserialize<Chapter>(request.RequestData);
                        existingChapter = _mySqlService.GetChapter(request.ChapterId.Value);
                        if (existingChapter == null) throw new Exception("Original chapter not found for edit.");

                        existingChapter.Number = chapterData.Number;
                        existingChapter.Title = chapterData.Title;
                        existingChapter.Content = chapterData.Content;
                        existingChapter.Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); // Update date on edit
                        _mySqlService.UpdateChapter(existingChapter);
                        break;
                    case ModerationRequestType.DeleteChapter:
                        if (!request.ChapterId.HasValue) throw new Exception("ChapterId is missing for delete request.");
                        _mySqlService.DeleteChapter(request.ChapterId.Value);
                        break;
                    default:
                        throw new InvalidOperationException("Неподдерживаемый тип запроса для глав.");
                }

                request.Status = ModerationStatus.Approved;
                request.ModeratorId = currentUser.Id;
                request.UpdatedAt = DateTime.UtcNow;
                _mySqlService.UpdateModerationRequest(request);

                // Send notification
                string chapterTitleForNotification = chapterData?.Title ?? existingChapter?.Title ?? "Неизвестная глава";
                string novelTitle = (request.NovelId.HasValue ? _mySqlService.GetNovel(request.NovelId.Value)?.Title : null) ?? "Неизвестная новелла";
                var message = $"Ваш запрос '{request.RequestType}' для главы '{chapterTitleForNotification}' (новелла '{novelTitle}') был одобрен.";

                // For AddChapter, chapterData.Id might be 0 if not refetched.
                // We need the actual ID of the newly created/approved chapter.
                int? relatedChapterId = request.ChapterId; // For Edit/Delete
                if (request.RequestType == ModerationRequestType.AddChapter)
                {
                    // This is tricky as CreateChapter doesn't return ID. We might need to fetch it.
                    // For now, we'll use the request ID or novel ID as related item if chapter ID isn't easily available.
                    // A better approach would be to ensure CreateChapter returns the ID or fetch last inserted ID for that novel.
                    // Using request.NovelId as a fallback related item.
                    relatedChapterId = _mySqlService.GetChaptersByNovel(request.NovelId.Value)
                                         .FirstOrDefault(c => c.Title == chapterData.Title && c.Number == chapterData.Number)?.Id ?? request.NovelId;
                }

                _notificationService.CreateNotification(request.UserId, NotificationType.ModerationApproved, message, relatedChapterId, RelatedItemType.Chapter);

                // Notification for new chapter to subscribers
                if (request.RequestType == ModerationRequestType.AddChapter && request.NovelId.HasValue)
                {
                    // Fetch chapter ID again, more reliably if possible
                    var newChapter = _mySqlService.GetChaptersByNovel(request.NovelId.Value)
                                         .FirstOrDefault(c => c.Title == chapterData.Title && c.Number == chapterData.Number && c.Content == chapterData.Content);
                    if (newChapter != null)
                    {
                        var subscribers = _mySqlService.GetUserIdsSubscribedToNovel(request.NovelId.Value, new List<string> { "reading", "read", "favorites" });
                        var newChapterMessage = $"Новая глава '{newChapter.Number} - {newChapter.Title}' добавлена к новелле '{novelTitle}'.";
                        foreach (var subId in subscribers)
                        {
                            if (subId != request.UserId) // Don't notify the chapter author themselves about their own new chapter via this path
                            {
                                _notificationService.CreateNotification(subId, NotificationType.NewChapter, newChapterMessage, newChapter.Id, RelatedItemType.Chapter);
                            }
                        }
                    }
                }

                return Json(new { success = true, message = $"Запрос главы ID {requestId} ({request.RequestType}) одобрен." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Ошибка обработки запроса главы ID {requestId}: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RejectChapterRequest(int requestId, string moderationComment)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !_permissionService.CanModerateChapterRequests(currentUser))
            {
                return Json(new { success = false, message = "Недостаточно прав." });
            }

            ModerationRequest request = _mySqlService.GetModerationRequestById(requestId);
            if (request == null || request.Status != ModerationStatus.Pending)
            {
                return Json(new { success = false, message = "Запрос не найден или уже обработан." });
            }

            request.Status = ModerationStatus.Rejected;
            request.ModeratorId = currentUser.Id;
            request.ModerationComment = moderationComment;
            request.UpdatedAt = DateTime.UtcNow;
            _mySqlService.UpdateModerationRequest(request);

            // Send notification
            string chapterTitleForNotification = "Неизвестная глава";
            string novelTitle = (request.NovelId.HasValue ? _mySqlService.GetNovel(request.NovelId.Value)?.Title : null) ?? "Неизвестная новелла";
            if (request.RequestType == ModerationRequestType.AddChapter && !string.IsNullOrEmpty(request.RequestData))
            {
                try { var ch = System.Text.Json.JsonSerializer.Deserialize<Chapter>(request.RequestData); chapterTitleForNotification = ch?.Title; } catch { }
            }
            else if (request.ChapterId.HasValue)
            {
                var existingCh = _mySqlService.GetChapter(request.ChapterId.Value); chapterTitleForNotification = existingCh?.Title;
            }

            var message = $"Ваш запрос '{request.RequestType}' для главы '{chapterTitleForNotification}' (новелла '{novelTitle}') был отклонен.";
            if (!string.IsNullOrWhiteSpace(moderationComment)) message += $" Причина: {moderationComment}";
            _notificationService.CreateNotification(request.UserId, NotificationType.ModerationRejected, message, request.ChapterId ?? request.NovelId, RelatedItemType.Chapter);

            return Json(new { success = true, message = $"Запрос главы ID {requestId} ({request.RequestType}) отклонен." });
        }
    }
}
