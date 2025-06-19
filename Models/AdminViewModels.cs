using System;
using System.Collections.Generic;
using BulbaLib.Models; // For ModerationRequestType and potentially Chapter related models

namespace BulbaLib.Models
{
    // Existing NovelModerationRequestViewModel - keeping it as is
    public class NovelModerationRequestViewModel
    {
        public int RequestId { get; set; }
        public ModerationRequestType RequestType { get; set; }
        public string RequestTypeFriendlyName => GetFriendlyRequestTypeName(RequestType);
        public int UserId { get; set; }
        public string RequesterLogin { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? NovelId { get; set; }
        public string NovelTitle { get; set; }
        public string Status { get; set; }
        public string RequestDataJson { get; set; }

        public Novel ProposedNovelData { get; set; }
        public Novel ExistingNovelData { get; set; }
        public NovelUpdateRequest? ProposedNovelChanges { get; set; }

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

    // Overwriting ChapterModerationRequestViewModel as per the new subtask's specific requirements
    public class ChapterModerationRequestViewModel
    {
        public int RequestId { get; set; }
        public ModerationRequestType RequestType { get; set; }
        public string UserLogin { get; set; } // Changed from RequesterLogin
        public int NovelId { get; set; }
        public string NovelTitle { get; set; }
        public string NovelCoverImageUrl { get; set; } // Added
        public int? ChapterId { get; set; } // For Edit/Delete existing chapter
        public string CurrentChapterNumber { get; set; } // For Edit/Delete, from existing chapter
        public string CurrentChapterTitle { get; set; } // For Edit/Delete, from existing chapter
        public string ProposedChapterNumber { get; set; } // For Add/Edit, from RequestData
        public string ProposedChapterTitle { get; set; } // For Add/Edit, from RequestData
        public DateTime RequestedAt { get; set; } // Changed from CreatedAt for consistency with prompt

        public string RequestActionDisplay // Property for display string
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

        // These fields were in the original prompt's version of ChapterModerationRequestViewModel.
        // They might be populated by the service or could be derived if RequestData is also exposed.
        // For now, including them as they were part of the target definition.
        // public string ParsedRequestDataChapterTitle { get; set; } // Replaced by ProposedChapterTitle
        // public string ParsedRequestDataChapterNumber { get; set; } // Replaced by ProposedChapterNumber
        public string ProposedChapterContent { get; set; } // For Add/Edit
        public string CurrentChapterContent { get; set; } // For Edit/Delete

        // Retaining this helper method if it's used by other view models or if it's a general utility.
        // If RequestActionDisplay replaces its usage for this VM, it could be removed if not used elsewhere.
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
