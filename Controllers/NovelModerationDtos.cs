// Using a separate file for DTOs related to novel moderation

using System.Collections.Generic;

namespace BulbaLib.Models // Assuming Models namespace is appropriate, or adjust as needed
{
    public class NovelEditModerationData
    {
        // We might not strictly need NovelId here if request.NovelId is always used,
        // but including it for completeness as it was part of the serialized data structure.
        public int NovelId { get; set; }
        public NovelPendingUpdateFields UpdatedFields { get; set; }
        public List<string>? KeptExistingCovers { get; set; }
        public List<string>? NewTempCoverPaths { get; set; }
    }

    public class NovelPendingUpdateFields
    {
        // Ensure these properties match what's in NovelsController's 
        // `updatedFieldsDataForModeration` anonymous object.
        // Nullable types should match the source.
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Genres { get; set; } // These are JSON strings
        public string? Tags { get; set; }   // These are JSON strings
        public string? Type { get; set; }
        public string? Format { get; set; }
        public int? ReleaseYear { get; set; }
        public int? AuthorId { get; set; }
        public string? AlternativeTitles { get; set; }
        public string? RelatedNovelIds { get; set; }
        // Add Id if it's part of UpdatedFields and used, though typically Id comes from the main request.
        // public int Id {get; set;} // From model.Id in NovelsController
    }

    public class NovelDeleteModerationData
    {
        public int Id { get; set; }
        public string? Title { get; set; } // Title might be included for context, though Id is key
    }
}
