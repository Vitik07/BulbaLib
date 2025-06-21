using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

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
        public IFormFile CoverFile { get; set; } // Оставляем, если используется для главной обложки

        [Display(Name = "Дополнительные обложки")]
        [ValidateNever]
        public List<IFormFile> NewCovers { get; set; } // Для загрузки нескольких обложек

        [Display(Name = "Автор")]
        public int? AuthorId { get; set; }

        [Display(Name = "Жанры")]
        public string? Genres { get; set; }

        [Display(Name = "Теги")]
        public string? Tags { get; set; }

        [Display(Name = "Тип")]
        [StringLength(100)]
        public string Type { get; set; }

        [Display(Name = "Формат")]
        [StringLength(100)]
        public string Format { get; set; }

        [Display(Name = "Год релиза")]
        [Range(1900, 2099, ErrorMessage = "Год релиза должен быть между 1900 и 2099.")]
        [RegularExpression(@"^\d{4}$", ErrorMessage = "Год релиза должен состоять из 4 цифр.")]
        public int? ReleaseYear { get; set; }

        [Display(Name = "Альтернативные названия")]
        [DataType(DataType.MultilineText)]
        public string? AlternativeTitles { get; set; }
        public string? RelatedNovelIds { get; set; }

        [Display(Name = "Сохранить как черновик")]
        public bool IsDraft { get; set; } // Для определения, является ли новелла черновиком

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