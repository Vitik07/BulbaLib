using System;
using System.Collections.Generic; // Required for List<string> in NovelUpdateRequest

namespace BulbaLib.Models
{
    // This class might need to be in its own file or a shared DTOs file
    // if it's not already defined elsewhere and accessible.
    // For this task, assuming it's accessible or defined as needed.
    // If NovelUpdateRequest is defined in e.g. NovelsController.cs,
    // it should ideally be moved to Models to be universally accessible.
    // For now, we'll assume it's available in the BulbaLib.Models namespace.
    public class NovelUpdateRequest
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public List<string>? Covers { get; set; } // List of cover URLs/paths
        public string? Genres { get; set; }
        public string? Tags { get; set; }
        public string? Type { get; set; }
        public string? Format { get; set; }
        public int? ReleaseYear { get; set; }
        public int? AuthorId { get; set; }
        public string? AlternativeTitles { get; set; }
    }

    // The definition of NovelModerationRequestViewModel has been removed from this file
    // to resolve CS0101, as it's also defined in Models/AdminViewModels.cs.
    // The class NovelUpdateRequest remains here for now.
}
