namespace BulbaLib.Models
{
    /*
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
    */

    public class ModerationRequest
    {
        [System.ComponentModel.DataAnnotations.Key] // Ensure Key attribute is recognized
        public int Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        public ModerationRequestType RequestType { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        public int UserId { get; set; }
        // public virtual User User { get; set; } // Navigation property, ensure User model is accessible

        public int? NovelId { get; set; }
        // public virtual Novel Novel { get; set; } // Navigation property

        public int? ChapterId { get; set; }
        // public virtual Chapter Chapter { get; set; } // Navigation property

        public string RequestData { get; set; } // JSON

        [System.ComponentModel.DataAnnotations.Required]
        public ModerationStatus Status { get; set; } = ModerationStatus.Pending;

        [System.ComponentModel.DataAnnotations.Required]
        public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;

        public int? ModeratorId { get; set; }
        // public virtual User Moderator { get; set; } // Navigation property

        public string ModerationComment { get; set; }

        public System.DateTime UpdatedAt { get; set; } = System.DateTime.UtcNow;
    }
}
