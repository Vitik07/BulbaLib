using System;

namespace BulbaLib.Models
{
    // Enum for Notification Types (can be expanded)
    /*
    public enum NotificationType
    {
        ModerationApproved,
        ModerationRejected,
        NewChapter,
        Generic // For other types of notifications
    }

    // Enum for Related Item Types (can be expanded)
    public enum RelatedItemType
    {
        Novel,
        Chapter,
        ModerationRequest,
        User
    }
    */
    public class Notification
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Type { get; set; } // Store as string, map to/from NotificationType enum in service/logic
        public string Message { get; set; }
        public int? RelatedItemId { get; set; }
        public string RelatedItemType { get; set; } // Store as string, map to/from RelatedItemType enum
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }

        // Optional: For display purposes, not mapped directly from DB if Type/RelatedItemType are strings
        // public NotificationType NotificationTypeValue => Enum.TryParse<NotificationType>(Type, true, out var result) ? result : default;
        // public RelatedItemType? RelatedItemTypeValue => Enum.TryParse<RelatedItemType>(RelatedItemType, true, out var result) ? result : (RelatedItemType?)null;
    }
}
