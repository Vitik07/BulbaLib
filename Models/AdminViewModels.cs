using System;
using System.Collections.Generic;
using BulbaLib.Models; // For ModerationRequestType, NovelStatus, etc.

namespace BulbaLib.Models
{
    // Updated NovelModerationRequestViewModel as per subtask requirements
    public class NovelModerationRequestViewModel
    {
        public int RequestId { get; set; }
        public ModerationRequestType RequestType { get; set; }
        public string UserLogin { get; set; }
        public int? NovelId { get; set; } // Nullable if AddNovel request data has no ID yet
        public string NovelTitle { get; set; }
        public string NovelCoverImageUrl { get; set; }
        public DateTime RequestedAt { get; set; }
        public ModerationStatus Status { get; set; } // Changed from string to enum

        // Detailed data for the details page
        public Novel ProposedNovelData { get; set; } // For AddNovel
        public EditNovelDataPayload ProposedEditData { get; set; } // For EditNovel
        public Novel CurrentNovelData { get; set; } // For EditNovel/DeleteNovel (original state)
        public List<string> TempCoverPreviews { get; set; } // Specifically for new temp covers in Add/Edit

        public string RequestActionDisplay
        {
            get
            {
                return RequestType switch
                {
                    ModerationRequestType.AddNovel => "Добавление новеллы",
                    ModerationRequestType.EditNovel => "Редактирование новеллы",
                    ModerationRequestType.DeleteNovel => "Удаление новеллы",
                    _ => RequestType.ToString(),
                };
            }
        }
    }

    // ChapterModerationRequestViewModel from previous subtask (ensure it's kept as is)
    public class ChapterModerationRequestViewModel
    {
        public int RequestId { get; set; }
        public ModerationRequestType RequestType { get; set; }
        public string UserLogin { get; set; }
        public int NovelId { get; set; }
        public string NovelTitle { get; set; }
        public string NovelCoverImageUrl { get; set; }
        public int? ChapterId { get; set; }
        public string CurrentChapterNumber { get; set; }
        public string CurrentChapterTitle { get; set; }
        public string ProposedChapterNumber { get; set; }
        public string ProposedChapterTitle { get; set; }
        public DateTime RequestedAt { get; set; }

        public string RequestActionDisplay
        {
            get
            {
                return RequestType switch
                {
                    ModerationRequestType.AddChapter => "Добавление главы",
                    ModerationRequestType.EditChapter => "Редактирование главы",
                    ModerationRequestType.DeleteChapter => "Удаление главы",
                    _ => RequestType.ToString(),
                };
            }
        }

        public string ProposedChapterContent { get; set; }
        public string CurrentChapterContent { get; set; }

        // Helper method (can be removed if not used by anything else, RequestActionDisplay handles its purpose for this VM)
        private static string GetFriendlyRequestTypeName(ModerationRequestType requestType)
        {
            switch (requestType)
            {
                case ModerationRequestType.AddNovel: return "Добавление новеллы";
                case ModerationRequestType.EditNovel: return "Редактирование новеллы";
                case ModerationRequestType.DeleteNovel: return "Удаление новеллы";
                case ModerationRequestType.AddChapter: return "Добавление главы";
                case ModerationRequestType.EditChapter: return "Редактирование главы";
                case ModerationRequestType.DeleteChapter: return "Удаление главы";
                default: return requestType.ToString();
            }
        }
    }
}
