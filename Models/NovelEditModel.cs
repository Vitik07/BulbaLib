using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BulbaLib.Models
{
    public class NovelEditModel
    {
        [Required]
        public int Id { get; set; }

        [Required(ErrorMessage = "Пожалуйста, введите название новеллы.")]
        [StringLength(200, MinimumLength = 1, ErrorMessage = "Название должно быть от 1 до 200 символов.")]
        public string Title { get; set; }

        [DataType(DataType.MultilineText)]
        public string Description { get; set; }

        public string Covers { get; set; } // JSON string array e.g., ["url1", "url2"]

        public string Genres { get; set; } // Comma-separated
        public string Tags { get; set; }   // Comma-separated

        [StringLength(100)]
        public string Type { get; set; }

        [StringLength(100)]
        public string Format { get; set; }

        [Range(1900, 2100, ErrorMessage = "Год выпуска должен быть между 1900 и 2100.")]
        public int? ReleaseYear { get; set; }

        public string AlternativeTitles { get; set; } // Comma-separated
        public string RelatedNovelIds { get; set; }   // Comma-separated IDs

        // To show who the original author is, not editable by Author role in this form.
        // Admin might be able to change it if UI/logic supports it.
        public int? AuthorId { get; set; }
        public string AuthorLogin { get; set; } // For display purposes
    }
}
