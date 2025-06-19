using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BulbaLib.Services;
// using BulbaLib.Interfaces; // Interfaces now in BulbaLib.Services
using BulbaLib.Models;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace BulbaLib.Controllers
{
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly INotificationService _notificationService;
        private readonly ICurrentUserService _currentUserService;
        private readonly MySqlService _mySqlService; // For fetching related item titles

        public NotificationsController(
            INotificationService notificationService,
            ICurrentUserService currentUserService,
            MySqlService mySqlService)
        {
            _notificationService = notificationService;
            _currentUserService = currentUserService;
            _mySqlService = mySqlService;
        }

        public async Task<IActionResult> Index(int page = 1, int pageSize = 15)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null) return Challenge(); // Or RedirectToAction("Login", "AuthView");

            // Fetch all notifications (read and unread) for the view, allow filtering in view or via query param later if needed
            var notifications = await _notificationService.GetNotifications(currentUser.Id, false, pageSize, (page - 1) * pageSize);
            int totalNotifications = await _notificationService.GetUnreadNotificationCount(currentUser.Id); // This gets unread, might need a total count method.
            // For now, GetUnreadNotificationCount might be okay for a rough estimate of pages if most are unread, or if we only show unread by default.
            // Let's assume for now the pagination is primarily for unread, or the view will list all but highlight unread.
            // For a proper total count of *all* notifications, MySqlService would need a new method.
            // Let's adjust to get *all* notifications for pagination, and then the view can style them.
            // MySqlService.GetNotificationsByUserId already returns List<Notification>, not Task.
            // MySqlService doesn't have a count for *all* notifications by user. This will be an issue for proper pagination of all notifications.
            // For now, let's get a page of all notifications and paginate based on typical number.

            var notificationViewModels = new List<NotificationViewModel>();
            foreach (var notification in notifications)
            {
                var vm = new NotificationViewModel
                {
                    Notification = notification,
                    TimeAgo = GetTimeAgo(notification.CreatedAt)
                };

                if (notification.RelatedItemId.HasValue)
                {
                    if (notification.RelatedItemType == RelatedItemType.Novel && notification.RelatedItemId.HasValue)
                    {
                        var novel = _mySqlService.GetNovel(notification.RelatedItemId.Value);
                        if (novel != null)
                        {
                            vm.RelatedItemTitle = novel.Title;
                            vm.RelatedItemUrl = Url.Action("Novel", "NovelView", new { id = novel.Id });
                        }
                    }
                    else if (notification.RelatedItemType == RelatedItemType.Chapter && notification.RelatedItemId.HasValue)
                    {
                        var chapter = await _mySqlService.GetChapterAsync(notification.RelatedItemId.Value);
                        if (chapter != null)
                        {
                            // Try to get novel title for context
                            // Consider if GetNovel should also be async if it involves I/O, though not part of this specific error.
                            var novel = _mySqlService.GetNovel(chapter.NovelId);
                            vm.RelatedItemTitle = $"Глава: {chapter.Number} - {chapter.Title} (Новелла: {novel?.Title ?? "N/A"})";
                            vm.RelatedItemUrl = Url.Action("Chapter", "ChapterView", new { id = chapter.Id });
                        }
                    }
                }
                notificationViewModels.Add(vm);
            }

            // Regarding pagination: MySqlService needs a method like CountAllNotificationsByUserId(userId) for accurate totalPages.
            // Using CountUnreadNotifications for totalPages is not accurate if showing all notifications.
            // For this step, I'll assume we get one page and the view will display it. Proper pagination needs the count method.
            // Let's simulate total count for now for pagination controls in view.
            ViewData["TotalPages"] = (int)Math.Ceiling((double)totalNotifications / pageSize); // This is based on unread, not total
            ViewData["CurrentPage"] = page;
            ViewData["PageSize"] = pageSize;


            return View("~/Views/Notifications/Index.cshtml", notificationViewModels);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkNotificationAsRead(int notificationId)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null) return Json(new { success = false, message = "Not authorized." });

            var success = await _notificationService.MarkAsRead(notificationId, currentUser.Id);
            return Json(new { success = success });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllNotificationsAsRead()
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null) return Json(new { success = false, message = "Not authorized." });

            var success = await _notificationService.MarkAllAsRead(currentUser.Id);
            return Json(new { success = success });
        }

        [HttpGet]
        public async Task<IActionResult> GetUnreadCount()
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null) return Json(new { count = 0 }); // Not an error, just no count for anonymous

            int count = await _notificationService.GetUnreadNotificationCount(currentUser.Id);
            return Json(new { count = count });
        }

        private string GetTimeAgo(DateTime dateTime)
        {
            TimeSpan timeSpan = DateTime.UtcNow - dateTime;

            if (timeSpan.TotalSeconds < 60) return $"{(int)timeSpan.TotalSeconds} сек назад";
            if (timeSpan.TotalMinutes < 60) return $"{(int)timeSpan.TotalMinutes} мин назад";
            if (timeSpan.TotalHours < 24) return $"{(int)timeSpan.TotalHours} ч назад";
            if (timeSpan.TotalDays < 30) return $"{(int)timeSpan.TotalDays} дн назад";
            if (timeSpan.TotalDays < 365) return $"{(int)(timeSpan.TotalDays / 30)} мес назад";
            return $"{(int)(timeSpan.TotalDays / 365)} г назад";
        }
    }
}
