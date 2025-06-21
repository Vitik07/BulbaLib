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
        private readonly MySqlService _mySqlService;

        public NotificationsController(
            INotificationService notificationService,
            ICurrentUserService currentUserService,
            MySqlService mySqlService)
        {
            _notificationService = notificationService;
            _currentUserService = currentUserService;
            _mySqlService = mySqlService;
        }

        // Action to provide data for the notifications modal
        public async Task<IActionResult> GetNotificationsModal(int page = 1, int pageSize = 10) // Default page size for modal
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null)
            {
                return PartialView("~/Views/Shared/_NotificationsModalPartial.cshtml", new List<NotificationViewModel>());
            }

            // Updated call to GetNotifications, removed 'false' argument for 'onlyUnread'
            var notifications = await _notificationService.GetNotifications(currentUser.Id, pageSize, (page - 1) * pageSize);

            var notificationViewModels = new List<NotificationViewModel>();
            foreach (var n in notifications)
            {
                string rejectionReason = null;
                if (n.Type == NotificationType.RequestRejected &&
                    n.RelatedItemType == RelatedItemType.ModerationRequest &&
                    n.RelatedItemId.HasValue)
                {
                    var moderationRequest = _mySqlService.GetModerationRequestById(n.RelatedItemId.Value);
                    if (moderationRequest != null)
                    {
                        // Используем поле Reason из самого уведомления, если оно есть.
                        // Если нет, то пытаемся получить из ModerationRequest.RejectionReason или ModerationComment.
                        rejectionReason = n.Reason;
                        if (string.IsNullOrEmpty(rejectionReason))
                        {
                            rejectionReason = moderationRequest.RejectionReason;
                            if (string.IsNullOrEmpty(rejectionReason))
                            {
                                rejectionReason = moderationRequest.ModerationComment;
                            }
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(n.Reason)) // Если это не ModerationRequest, но причина есть в уведомлении
                {
                    rejectionReason = n.Reason;
                }


                notificationViewModels.Add(new NotificationViewModel
                {
                    Id = n.Id,
                    UserId = n.UserId,
                    Type = MapNotificationTypeToRussian(n.Type),
                    Message = n.Message,
                    Link = await GetNotificationLink(n),
                    DateSent = n.CreatedAt,
                    RejectionReason = rejectionReason // Это поле теперь содержит причину из Notification.Reason или из ModerationRequest
                    // IsRead property removed from NotificationViewModel and Notification model
                });
            }

            // Pagination data for modal, if needed by the partial view
            // int totalUserNotifications = _mySqlService.CountUserNotifications(currentUser.Id); 
            // ViewData["TotalPagesModal"] = (int)Math.Ceiling((double)totalUserNotifications / pageSize);
            // ViewData["CurrentPageModal"] = page;
            // ViewData["PageSizeModal"] = pageSize;

            return PartialView("~/Views/Shared/_NotificationsModalPartial.cshtml", notificationViewModels);
        }

        private async Task<string> GetNotificationLink(Notification notification)
        {
            if (!notification.RelatedItemId.HasValue) return null;

            switch (notification.RelatedItemType)
            {
                case RelatedItemType.Novel:
                    // Link to the novel page
                    return Url.Action("Novel", "NovelView", new { id = notification.RelatedItemId.Value });
                case RelatedItemType.Chapter:
                    // Link to the chapter page
                    // Requires NovelId. Fetch chapter details to get NovelId if not stored in notification.
                    // For simplicity, assuming chapterId is enough, or that novelId is also part of notification if needed.
                    // If ChapterView route is /Novel/{novelId}/Chapter/{id}
                    var chapter = await _mySqlService.GetChapterAsync(notification.RelatedItemId.Value);
                    if (chapter != null)
                    {
                        return Url.Action("Chapter", "ChapterView", new { novelId = chapter.NovelId, id = chapter.Id });
                    }
                    return null;
                default:
                    return null;
            }
        }

        private string MapNotificationTypeToRussian(NotificationType type)
        {
            // Using DisplayName attribute would be cleaner if enums are annotated.
            // For example: typeof(NotificationType).GetMember(type.ToString()).First().GetCustomAttribute<DisplayAttribute>()?.GetName()
            // Direct mapping for now:
            return type switch
            {
                NotificationType.RequestApproved => "Запрос одобрен", // New general "approved" title
                NotificationType.RequestRejected => "Запрос отклонен", // New general "rejected" title
                // Старые типы, если они еще используются для других целей:
                NotificationType.ModerationApproved => "Запрос на модерацию одобрен",
                NotificationType.ModerationRejected => "Запрос на модерацию отклонен",
                NotificationType.NewChapter => "Новая глава",
                NotificationType.NovelUpdated => "Новелла обновлена",
                _ => type.ToString(),
            };
        }

        // Removed MarkNotificationAsRead, MarkAllNotificationsAsRead, and GetUnreadCount actions

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
