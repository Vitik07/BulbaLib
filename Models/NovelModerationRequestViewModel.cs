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

    public class NovelModerationRequestViewModel
    {
        public int RequestId { get; set; }
        public ModerationRequestType RequestType { get; set; }
        // public string RequestTypeDisplay { get; set; } // Not assigned in controller
        public string RequestTypeFriendlyName
        {
            get
            {
                switch (RequestType)
                {
                    case ModerationRequestType.AddNovel: return "Добавление новеллы";
                    case ModerationRequestType.EditNovel: return "Редактирование новеллы";
                    case ModerationRequestType.DeleteNovel: return "Удаление новеллы";
                    default: return RequestType.ToString();
                }
            }
        }
        public int UserId { get; set; }
        public string? RequesterLogin { get; set; }
        public DateTime CreatedAt { get; set; }

        public int? NovelId { get; set; }
        public string? NovelTitle { get; set; }

        public string? RequestDataJson { get; set; }
        public Novel? ProposedNovelData { get; set; }
        public NovelUpdateRequest? ProposedNovelChanges { get; set; } // Added this property

        // public Novel ExistingNovelData { get; set; } // Not assigned in the relevant controller part

        public string? Status { get; set; }

        // public string ChangeSummary { get; set; } // Not assigned in controller
    }
}
