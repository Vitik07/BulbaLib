using System.ComponentModel.DataAnnotations;

namespace BulbaLib.Models
{
    public enum NotificationType
    {
        [Display(Name = "Запрос одобрен")]
        RequestApproved, // Renamed from ModerationApproved for clarity with plan
        [Display(Name = "Запрос отклонен")]
        RequestRejected, // Renamed from ModerationRejected for clarity with plan
        [Display(Name = "Новая глава")]
        NewChapter,
        [Display(Name = "Новелла обновлена")] // Added as per plan step 3
        NovelUpdated
    }
}
