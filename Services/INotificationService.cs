using BulbaLib.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BulbaLib.Services
{
    public interface INotificationService
    {
        Task CreateNotification(int userId, NotificationType type, string message, int? relatedItemId = null, RelatedItemType? relatedItemType = null);
        Task<List<Notification>> GetUnreadNotifications(int userId); // This could potentially be removed if GetNotifications can handle "onlyUnread"
        Task<List<Notification>> GetNotifications(int userId, bool onlyUnread, int limit, int offset); // Added onlyUnread
        Task<int> GetUnreadNotificationCount(int userId);
        Task<bool> MarkAsRead(int notificationId, int userId);
        Task<bool> MarkAllAsRead(int userId);
    }
}
