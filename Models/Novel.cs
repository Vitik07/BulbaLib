using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Models
{
    public class Novel
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Covers { get; set; }
        public string Genres { get; set; }
        public string Tags { get; set; }
        public string Type { get; set; }
        public string Format { get; set; }
        public int? ReleaseYear { get; set; }
        public int? AuthorId { get; set; }
        public string AlternativeTitles { get; set; }
        public string? RelatedNovelIds { get; set; }
        public long Date { get; set; }
        public NovelStatus Status { get; set; }
        public string TranslatorIds { get; set; } = "[]"; // For storing JSON array of translator user IDs

        public List<string> CoversList
        {
            get => string.IsNullOrWhiteSpace(Covers)
                ? new List<string>()
                : System.Text.Json.JsonSerializer.Deserialize<List<string>>(Covers);
            set => Covers = System.Text.Json.JsonSerializer.Serialize(value ?? new List<string>());
        }
    }
}