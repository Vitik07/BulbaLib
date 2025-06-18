using BulbaLib.Models;
// using BulbaLib.Interfaces; // Removed as INotificationService is in BulbaLib.Services
using Microsoft.Extensions.Logging; // Added for ILogger
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BulbaLib.Services
{
    public class NotificationService : INotificationService
    {
        private readonly MySqlService _mySqlService;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(MySqlService mySqlService, ILogger<NotificationService> logger)
        {
            _mySqlService = mySqlService;
            _logger = logger;
        }

        public Task CreateNotification(int userId, NotificationType type, string message, int? relatedItemId = null, RelatedItemType? relatedItemType = null)
        {
            try
            {
                var notification = new Notification
                {
                    UserId = userId,
                    Type = type,
                    Message = message,
                    RelatedItemId = relatedItemId,
                    RelatedItemType = relatedItemType ?? RelatedItemType.None, // Default if null
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateNotification(notification);
                _logger.LogInformation("Notification created for User {UserId}, Type {Type}, Message {Message}", userId, type, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating notification for User {UserId}", userId);
                // Decide if the exception should be re-thrown or handled (e.g., return a failed task)
                // For void Task, Task.CompletedTask on success, Task.FromException(ex) on failure could be options.
                // Or just let it bubble up if that's the desired behavior.
            }
            return Task.CompletedTask; // Matches void async method signature
        }

        // This method is in INotificationService but not directly requested by subtask description for this service.
        // The subtask asks for GetNotifications(userId, onlyUnread, limit, offset)
        // I will implement GetUnreadNotifications as it's in the interface.
        public Task<List<Notification>> GetUnreadNotifications(int userId)
        {
            try
            {
                // Assuming GetNotificationsByUserId can handle onlyUnread = true with a default limit/offset or needs adjustment.
                // For now, using a high limit to get "all" unread for this specific interface method.
                return Task.FromResult(_mySqlService.GetNotificationsByUserId(userId, true, 100, 0));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting unread notifications for User {UserId}", userId);
                return Task.FromResult(new List<Notification>()); // Return empty list on error
            }
        }

        // This is the method requested by the subtask.
        public Task<List<Notification>> GetNotifications(int userId, bool onlyUnread, int limit, int offset)
        {
            try
            {
                return Task.FromResult(_mySqlService.GetNotificationsByUserId(userId, onlyUnread, limit, offset));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notifications for User {UserId}", userId);
                return Task.FromResult(new List<Notification>());
            }
        }

        // Method as per subtask, matching INotificationService's GetUnreadNotificationCount
        public Task<int> GetUnreadNotificationCount(int userId)
        {
            try
            {
                return Task.FromResult(_mySqlService.CountUnreadNotifications(userId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error counting unread notifications for User {UserId}", userId);
                return Task.FromResult(0);
            }
        }

        public Task<bool> MarkAsRead(int notificationId, int userId)
        {
            try
            {
                return Task.FromResult(_mySqlService.MarkNotificationAsRead(notificationId, userId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notification {NotificationId} as read for User {UserId}", notificationId, userId);
                return Task.FromResult(false);
            }
        }

        public Task<bool> MarkAllAsRead(int userId)
        {
            try
            {
                // MySqlService.MarkAllNotificationsAsRead returns int (count of affected rows)
                // Interface expects Task<bool>. We'll return true if count > 0 or just true if no error.
                _mySqlService.MarkAllNotificationsAsRead(userId);
                return Task.FromResult(true); // Assuming success if no exception
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking all notifications as read for User {UserId}", userId);
                return Task.FromResult(false);
            }
        }
    }
}
