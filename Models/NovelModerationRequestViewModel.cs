using System;

namespace BulbaLib.Models
{
    /*
    public class NovelModerationRequestViewModel
    {
        public int RequestId { get; set; }
        public ModerationRequestType RequestType { get; set; }
        public string RequestTypeDisplay { get; set; } // e.g., "Добавление", "Редактирование"
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
        public string RequesterLogin { get; set; }
        public DateTime CreatedAt { get; set; }

        public int? NovelId { get; set; } // Existing Novel ID for Edit/Delete
        public string NovelTitle { get; set; } // Title of the novel involved

        public string RequestDataJson { get; set; } // Raw JSON data for inspection or complex previews
        public Novel ProposedNovelData { get; set; } // Deserialized novel data for Add/Edit
        public Novel ExistingNovelData { get; set; } // For comparing in Edit requests

        public string Status { get; set; } // Pending, Approved, Rejected

        // Helper to get a summary of changes for Edit requests
        public string ChangeSummary { get; set; }
    }
    */
}
