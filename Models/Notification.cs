using System;

namespace BulbaLib.Models
{
    public class Notification
    {
        public int Id { get; set; }
        public int UserId { get; set; } // Кому уведомление
        public NotificationType Type { get; set; }
        public string Message { get; set; }
        public int? RelatedItemId { get; set; }
        public RelatedItemType RelatedItemType { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Reason { get; set; } // Причина для уведомления (например, при отклонении)
    }
}
