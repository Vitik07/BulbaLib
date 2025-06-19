using System;
using System.Collections.Generic;
// Ensure BulbaLib.Models is referenced if Novel, Chapter etc. are in that base namespace.
// If they are in BulbaLib.Models.ViewModels, this using might not be strictly necessary
// but doesn't harm. Given the other files, they seem to be in BulbaLib.Models.
using BulbaLib.Models;

// The namespace for these ViewModels should ideally be consistent.
// If other models are in BulbaLib.Models, then BulbaLib.Models or BulbaLib.Models.ViewModels is fine.
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

    public class ChapterModerationRequestViewModel // This is the single definition
    {
        public int RequestId { get; set; }
        public ModerationRequestType RequestType { get; set; } // This was line 43 from the error
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
        public string ProposedContent { get; set; }
        public Chapter ExistingChapterData { get; set; }
        public string ExistingContent { get; set; }

        public string? ModerationComment { get; set; }
        public DateTime? UpdatedAt { get; set; }

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
