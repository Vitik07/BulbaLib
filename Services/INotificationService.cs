using BulbaLib.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BulbaLib.Services
{
    public interface INotificationService
    {
        Task CreateNotification(int userId, NotificationType type, string message, int? relatedItemId = null, RelatedItemType? relatedItemType = null, string reason = null);
        Task<List<Notification>> GetNotifications(int userId, int limit, int offset);
        Task CreateNotificationForSubscribedUsers(int novelId, int chapterId, string novelTitle, string chapterNumberOrTitle);
    }
}