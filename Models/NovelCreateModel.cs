using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System;
using Microsoft.AspNetCore.Http; // Added for IFormFile

namespace BulbaLib.Models
{
    public class NovelCreateModel : IValidatableObject
    {
        [Required(ErrorMessage = "Пожалуйста, введите название новеллы.")]
        [StringLength(200, MinimumLength = 1, ErrorMessage = "Название должно быть от 1 до 200 символов.")]
        public string Title { get; set; }

        [DataType(DataType.MultilineText)]
        public string Description { get; set; }

        [Display(Name = "Основная обложка")]
        public IFormFile CoverFile { get; set; }
        // public string Covers { get; set; } // Old property commented out or removed

        [Display(Name = "Жанры")]
        public string Genres { get; set; } // Comma-separated or JSON string
        [Display(Name = "Теги")]
        public string Tags { get; set; }   // Comma-separated or JSON string

        [Display(Name = "Тип")]
        [StringLength(100)]
        public string Type { get; set; } // e.g., Web Novel, Light Novel

        [Display(Name = "Формат")]
        [StringLength(100)]
        public string Format { get; set; } // e.g., Original, Translation

        [Display(Name = "Год релиза")]
        [Range(1900, 2099, ErrorMessage = "Год релиза должен быть между 1900 и 2099.")] // Обновленный статический диапазон
        [RegularExpression(@"^\d{4}$", ErrorMessage = "Год релиза должен состоять из 4 цифр.")]
        // Примечание: Более точная валидация "не позже текущего года" будет добавлена позже, если потребуется, на уровне контроллера или IValidatableObject.
        public int? ReleaseYear { get; set; }

        [Display(Name = "Альтернативные названия")]
        [DataType(DataType.MultilineText)]
        public string AlternativeTitles { get; set; } // Comma-separated
        public string RelatedNovelIds { get; set; }   // Comma-separated IDs

        // AuthorId will be set in the controller based on the current user
        // TranslatorId can be set by Admin or later

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (ReleaseYear.HasValue && ReleaseYear.Value > DateTime.UtcNow.Year)
            {
                yield return new ValidationResult(
                    $"Год релиза не может быть позже текущего года ({DateTime.UtcNow.Year}).",
                    new[] { nameof(ReleaseYear) });
            }
        }
    }
}
