using BulbaLib.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BulbaLib.Services
{
    public interface INotificationService
    {
        Task CreateNotification(int userId, NotificationType type, string message, int? relatedItemId = null, RelatedItemType? relatedItemType = null);
        Task<List<Notification>> GetNotifications(int userId, int limit, int offset);
    }
}
