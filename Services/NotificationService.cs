using BulbaLib.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BulbaLib.Services
{
    public class NotificationService : INotificationService // Added inheritance
    {
        // Stub implementations for interface methods
        public Task CreateNotification(int userId, NotificationType type, string message, int? relatedItemId = null, RelatedItemType? relatedItemType = null)
        {
            Console.WriteLine($"Notification created for User {userId}: Type: {type}, Message: {message}, ItemId: {relatedItemId}, ItemType: {relatedItemType}");
            // throw new NotImplementedException(); // Or just return Task.CompletedTask for void async methods
            return Task.CompletedTask;
        }

        public Task<List<Notification>> GetUnreadNotifications(int userId)
        {
            Console.WriteLine($"GetUnreadNotifications called for User {userId}");
            // throw new NotImplementedException();
            return Task.FromResult(new List<Notification>());
        }

        public Task<List<Notification>> GetNotifications(int userId, int limit, int offset)
        {
            Console.WriteLine($"GetNotifications called for User {userId}, Limit: {limit}, Offset: {offset}");
            // throw new NotImplementedException();
            return Task.FromResult(new List<Notification>());
        }

        public Task<int> GetUnreadNotificationCount(int userId)
        {
            Console.WriteLine($"GetUnreadNotificationCount called for User {userId}");
            // throw new NotImplementedException();
            return Task.FromResult(0);
        }

        public Task<bool> MarkAsRead(int notificationId, int userId)
        {
            Console.WriteLine($"MarkAsRead called for Notification {notificationId}, User {userId}");
            // throw new NotImplementedException();
            return Task.FromResult(true);
        }

        public Task<bool> MarkAllAsRead(int userId)
        {
            Console.WriteLine($"MarkAllAsRead called for User {userId}");
            // throw new NotImplementedException();
            return Task.FromResult(true);
        }
    }
}
