using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System;
using Microsoft.AspNetCore.Http;

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

        [Display(Name = "Существующие обложки")]
        public List<string> ExistingCoverPaths { get; set; } = new List<string>(); // Переименовано из KeptCovers

        [Display(Name = "Обложки к удалению")]
        public List<string> CoversToDelete { get; set; } = new List<string>(); // Новое свойство

        [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]
        [Display(Name = "Загрузить новые обложки")]
        public List<IFormFile>? NewCovers { get; set; } // Переименовано из NewCoverFiles

        [Display(Name = "Жанры")]
        public string Genres { get; set; }

        [Display(Name = "Теги")]
        public string Tags { get; set; }

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
        [Range(1900, 2099, ErrorMessage = "Год релиза должен быть между 1900 и 2099.")]
        [RegularExpression(@"^\d{4}$", ErrorMessage = "Год релиза должен состоять из 4 цифр.")]
        public int? ReleaseYear { get; set; }

        [Display(Name = "Альтернативные названия")]
        [DataType(DataType.MultilineText)]
        public string AlternativeTitles { get; set; }
        public string RelatedNovelIds { get; set; }

        [Display(Name = "Автор")]
        public int? AuthorId { get; set; }
        public string? AuthorLogin { get; set; }

        [Display(Name = "Сохранить как черновик")]
        public bool IsDraft { get; set; } // Новое свойство

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (ReleaseYear.HasValue && ReleaseYear.Value > DateTime.UtcNow.Year)
            {
                yield return new ValidationResult(
                    $"Год релиза не может быть позже текущего года ({DateTime.UtcNow.Year}).",
                    new[] { nameof(ReleaseYear) });
            }

            // Обновленная логика валидации обложек
            // bool hasExistingCovers = ExistingCoverPaths != null && ExistingCoverPaths.Any(c => !string.IsNullOrWhiteSpace(c)); // Старая переменная, не используется в новой логике напрямую
            bool hasNewCoverFiles = NewCovers != null && NewCovers.Any(f => f != null && f.Length > 0);
            // int coversToDeleteCount = CoversToDelete != null ? CoversToDelete.Count : 0; // coversToDeleteCount больше не нужен для этой проверки

            // Новая логика: ExistingCoverPaths от клиента уже содержит только те обложки, которые должны остаться.
            // Считаем количество действительных путей в ExistingCoverPaths.
            int netExistingCovers = ExistingCoverPaths != null ? ExistingCoverPaths.Count(c => !string.IsNullOrWhiteSpace(c)) : 0;

            if (netExistingCovers <= 0 && !hasNewCoverFiles)
            {
                yield return new ValidationResult(
                   "Новелла должна иметь хотя бы одну обложку. Пожалуйста, загрузите новую или убедитесь, что не все существующие обложки помечены на удаление.",
                   // Оставляем все три поля, так как они все влияют на конечное состояние обложек
                   new[] { nameof(ExistingCoverPaths), nameof(NewCovers), nameof(CoversToDelete) });
            }
        }
    }
}
