using System.ComponentModel.DataAnnotations;

namespace BulbaLib.Models
{
    public enum ModerationRequestType
    {
        [Display(Name = "Добавление новеллы")]
        AddNovel,
        [Display(Name = "Редактирование новеллы")]
        EditNovel,
        [Display(Name = "Удаление новеллы")]
        DeleteNovel,
        [Display(Name = "Добавление главы")]
        AddChapter,
        [Display(Name = "Редактирование главы")]
        EditChapter,
        [Display(Name = "Удаление главы")]
        DeleteChapter
    }
}
