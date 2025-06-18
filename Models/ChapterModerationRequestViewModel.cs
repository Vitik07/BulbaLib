using System;

namespace BulbaLib.Models
{
    /*
    public class ChapterModerationRequestViewModel
    {
        public int RequestId { get; set; }
        public ModerationRequestType RequestType { get; set; }
        public string RequestTypeDisplay { get; set; } // e.g., "Добавление Главы"
        public string RequestTypeFriendlyName
        {
            get
            {
                switch (RequestType)
                {
                    case ModerationRequestType.AddChapter: return "Добавление главы";
                    case ModerationRequestType.EditChapter: return "Редактирование главы";
                    case ModerationRequestType.DeleteChapter: return "Удаление главы";
                    default: return RequestType.ToString();
                }
            }
        }
        public int UserId { get; set; }
        public string RequesterLogin { get; set; }
        public DateTime CreatedAt { get; set; }

        public int? NovelId { get; set; }
        public string NovelTitle { get; set; }

        public int? ChapterId { get; set; } // Existing Chapter ID for Edit/Delete
        public string ChapterNumber { get; set; } // From proposed or existing chapter
        public string ChapterTitle { get; set; }  // From proposed or existing chapter

        public string RequestDataJson { get; set; } // Raw JSON data
        public Chapter ProposedChapterData { get; set; } // Deserialized chapter data for Add/Edit
        public Chapter ExistingChapterData { get; set; } // For comparing in Edit requests

        public string Status { get; set; }

        // Helper to get a summary of changes for Edit requests (optional)
        public string ChangeSummary { get; set; }
    }
    */
}
