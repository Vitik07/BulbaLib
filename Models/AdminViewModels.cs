using System;
using System.Collections.Generic;
using BulbaLib.Models; // For Novel, Chapter, ModerationRequestType

// It's better to have these in BulbaLib.Models or a sub-namespace like BulbaLib.Models.ViewModels

namespace BulbaLib.Models // Or BulbaLib.Models.ViewModels
{
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
        // This was specific to how NovelsController constructed edit requests.
        // For admin panel, if RequestData for EditNovel is NovelUpdateRequest,
        // the AdminController.NovelRequestDetails action will handle mapping this to ProposedNovelData (as type Novel) or specific fields.
        // public NovelUpdateRequest ProposedNovelChanges { get; set; } 

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

    public class ChapterModerationRequestViewModel
    {
        public int RequestId { get; set; }
        public ModerationRequestType RequestType { get; set; }
        public string RequestTypeFriendlyName => GetFriendlyRequestTypeName(RequestType);
        public int UserId { get; set; }
        public string RequesterLogin { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? NovelId { get; set; }
        public string NovelTitle { get; set; }
        public int? ChapterId { get; set; }
        public string ChapterNumber { get; set; }
        public string ChapterTitle { get; set; }
        public string Status { get; set; }
        public string RequestDataJson { get; set; }

        public Chapter ProposedChapterData { get; set; }
        public string ProposedContent { get; set; } // Extracted from ProposedChapterData.Content for convenience
        public Chapter ExistingChapterData { get; set; }
        public string ExistingContent { get; set; } // Loaded from file for convenience

        private static string GetFriendlyRequestTypeName(ModerationRequestType requestType)
        {
            // Consider moving to a shared helper if used in multiple ViewModels
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
