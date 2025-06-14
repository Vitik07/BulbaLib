using System.ComponentModel.DataAnnotations;

namespace BulbaLib.Models
{
    public class ChapterCreateModel
    {
        [Required]
        public int NovelId { get; set; }

        [Display(Name = "Номер главы")]
        [Required(ErrorMessage = "Пожалуйста, укажите номер главы (например, 'Глава 1' или 'Том 1 Глава 1').")]
        [StringLength(100, ErrorMessage = "Номер главы должен быть до 100 символов.")]
        public string Number { get; set; } // e.g., "Chapter 1", "Vol.1 Chapter 1"

        [Display(Name = "Название главы")]
        [Required(ErrorMessage = "Пожалуйста, введите название главы.")]
        [StringLength(200, ErrorMessage = "Название главы должно быть до 200 символов.")]
        public string Title { get; set; }

        [Display(Name = "Содержимое главы")]
        [Required(ErrorMessage = "Пожалуйста, введите содержимое главы.")]
        [DataType(DataType.MultilineText)]
        public string Content { get; set; }

        // TranslatorId will be set in the controller from the current user if they are a Translator
    }
}
