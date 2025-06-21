using System.ComponentModel.DataAnnotations;

namespace BulbaLib.Models
{
    public enum NotificationType
    {
        [Display(Name = "Запрос одобрен")]
        RequestApproved,
        [Display(Name = "Запрос отклонен")]
        RequestRejected,
        [Display(Name = "Новая глава")]
        NewChapter,
        [Display(Name = "Новелла обновлена")]
        NovelUpdated,
        [Display(Name = "Запрос на модерацию одобрен")]
        ModerationApproved, // To handle existing DB values
        [Display(Name = "Запрос на модерацию отклонен")]
        ModerationRejected  // To handle existing DB values
    }
}
