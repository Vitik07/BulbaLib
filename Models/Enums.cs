namespace BulbaLib.Models
{

    public enum ModerationRequestType
    {
        NovelCreate,
        NovelUpdate,
        NovelDelete,
        ChapterCreate,
        ChapterUpdate,
        ChapterDelete
    }

    public enum ModerationStatus
    {
        Pending,
        Approved,
        Rejected
    }

    public enum NotificationType
    {
        ModerationApproved,
        ModerationRejected,
        NewChapter
        // Add other types as needed
    }

    public enum RelatedItemType
    {
        Novel,
        Chapter
        // Add other types as needed
    }

    public enum UserRole
    {
        User,
        Admin,
        Author,
        Translator
    }
}
