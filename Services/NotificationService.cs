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

        public async Task CreateNotificationForSubscribedUsers(int novelId, int chapterId, string novelTitle, string chapterNumberOrTitle)
        {
            // Статусы, при которых пользователь считается подписанным на уведомления о новых главах
            var subscribedStatuses = new List<string> { "Читаю", "Прочитано", "Любимое" }; // TODO: Использовать enum или константы

            var userIds = _mySqlService.GetUserIdsSubscribedToNovel(novelId, subscribedStatuses);

            foreach (var userId in userIds)
            {
                string message = $"Вышла новая глава ({chapterNumberOrTitle}) новеллы \"{novelTitle}\".";
                await CreateNotification(userId, NotificationType.NewChapter, message, chapterId, RelatedItemType.Chapter);
            }
        }

        public Task<List<Notification>> GetNotifications(int userId, int limit, int offset)
        {
            try
            {
                // Call the updated MySqlService method which no longer takes 'onlyUnread'
                return Task.FromResult(_mySqlService.GetNotificationsByUserId(userId, limit, offset));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notifications for User {UserId}", userId);
                return Task.FromResult(new List<Notification>());
            }
        }
    }
}
