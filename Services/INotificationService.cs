using BulbaLib.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BulbaLib.Services
{
    public interface INotificationService
    {
        Task CreateNotification(int userId, NotificationType type, string message, int? relatedItemId = null, RelatedItemType? relatedItemType = null);
        Task<List<Notification>> GetUnreadNotifications(int userId);
        Task<List<Notification>> GetNotifications(int userId, int limit, int offset);
        Task<int> GetUnreadNotificationCount(int userId);
        Task<bool> MarkAsRead(int notificationId, int userId);
        Task<bool> MarkAllAsRead(int userId);
    }
}
