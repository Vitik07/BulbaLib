namespace BulbaLib.Models
{

    public enum ModerationRequestType
    {
        AddNovel,
        EditNovel,
        DeleteNovel,
        AddChapter,
        EditChapter,
        DeleteChapter
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
