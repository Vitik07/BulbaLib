using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BulbaLib.Models
{
    public class NovelCreateModel
    {
        [Required(ErrorMessage = "Пожалуйста, введите название новеллы.")]
        [StringLength(200, MinimumLength = 1, ErrorMessage = "Название должно быть от 1 до 200 символов.")]
        public string Title { get; set; }

        [DataType(DataType.MultilineText)]
        public string Description { get; set; }

        // For simplicity, covers are handled as a JSON string of URLs.
        // Actual upload mechanism will be separate.
        public string Covers { get; set; } // Should be a JSON string array e.g., ["url1", "url2"] or single URL

        public string Genres { get; set; } // Comma-separated
        public string Tags { get; set; }   // Comma-separated

        [StringLength(100)]
        public string Type { get; set; } // e.g., Web Novel, Light Novel

        [StringLength(100)]
        public string Format { get; set; } // e.g., Original, Translation

        [Range(1900, 2100, ErrorMessage = "Год выпуска должен быть между 1900 и 2100.")]
        public int? ReleaseYear { get; set; }

        public string AlternativeTitles { get; set; } // Comma-separated
        public string RelatedNovelIds { get; set; }   // Comma-separated IDs

        // AuthorId will be set in the controller based on the current user
        // TranslatorId can be set by Admin or later
    }
}
