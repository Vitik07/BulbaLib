using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System;
using Microsoft.AspNetCore.Http; // Added for IFormFile

namespace BulbaLib.Models
{
    public class NovelEditModel : IValidatableObject
    {
        [Required]
        public int Id { get; set; }

        [Required(ErrorMessage = "Пожалуйста, введите название новеллы.")]
        [StringLength(200, MinimumLength = 1, ErrorMessage = "Название должно быть от 1 до 200 символов.")]
        public string Title { get; set; }

        [Required(ErrorMessage = "Пожалуйста, введите описание новеллы.")]
        [DataType(DataType.MultilineText)]
        public string Description { get; set; }

        public List<string> KeptCovers { get; set; } = new List<string>(); // Renamed from Covers

        [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]
        [Display(Name = "Загрузить новые обложки")]
        public List<IFormFile>? NewCoverFiles { get; set; }

        [Display(Name = "Жанры")]
        public string Genres { get; set; } // Comma-separated or JSON string
        [Display(Name = "Теги")]
        public string Tags { get; set; }   // Comma-separated or JSON string

        [Required(ErrorMessage = "Пожалуйста, выберите тип новеллы.")]
        [Display(Name = "Тип")]
        [StringLength(100)]
        public string Type { get; set; }

        [Required(ErrorMessage = "Пожалуйста, выберите формат новеллы.")]
        [Display(Name = "Формат")]
        [StringLength(100)]
        public string Format { get; set; }

        [Required(ErrorMessage = "Пожалуйста, укажите год релиза.")]
        [Display(Name = "Год релиза")]
        [Range(1900, 2099, ErrorMessage = "Год релиза должен быть между 1900 и 2099.")] // Обновленный статический диапазон
        [RegularExpression(@"^\d{4}$", ErrorMessage = "Год релиза должен состоять из 4 цифр.")]
        public int? ReleaseYear { get; set; }

        [Display(Name = "Альтернативные названия")]
        [DataType(DataType.MultilineText)]
        public string AlternativeTitles { get; set; } // Comma-separated
        public string RelatedNovelIds { get; set; }   // Comma-separated IDs

        // To show who the original author is, not editable by Author role in this form.
        // Admin might be able to change it if UI/logic supports it.
        [Display(Name = "Автор")]
        public int? AuthorId { get; set; }
        public string? AuthorLogin { get; set; } // For display purposes

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (ReleaseYear.HasValue && ReleaseYear.Value > DateTime.UtcNow.Year)
            {
                yield return new ValidationResult(
                    $"Год релиза не может быть позже текущего года ({DateTime.UtcNow.Year}).",
                    new[] { nameof(ReleaseYear) });
            }

            // Check for at least one cover
            bool hasExistingCovers = KeptCovers != null && KeptCovers.Any(c => !string.IsNullOrWhiteSpace(c)); // Updated to KeptCovers
            bool hasNewCoverFiles = NewCoverFiles != null && NewCoverFiles.Any(f => f != null && f.Length > 0);

            if (!hasExistingCovers && !hasNewCoverFiles)
            {
                // Attempt to add the error to a specific member if possible,
                // or as a model-level error.
                // For covers, it might relate to NewCoverFiles or a general model error.
                // Let's try to associate it with NewCoverFiles for UI display,
                // though it's a combined condition.
                yield return new ValidationResult(
                    "Новелла должна иметь хотя бы одну обложку. Пожалуйста, загрузите новую или убедитесь, что существующая обложка не удалена.",
                    new string[] { }); // Or use an empty array for model-level error: new string[] { }
            }
        }
    }
}
