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
        public string ModerationComment { get; set; } // Added for admin comment

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

    public class ChapterModerationRequestViewModel
    {
        // Base Properties
        public int RequestId { get; set; }
        public ModerationRequestType RequestType { get; set; }
        public string RequestDataJson { get; set; }
        public string Status { get; set; }
        public int NovelId { get; set; }
        public string NovelTitle { get; set; }
        public string NovelCoverImageUrl { get; set; }
        public int? ChapterId { get; set; }

        // User Related
        public int UserId { get; set; }
        public string UserLogin { get; set; }       // Added back
        public string RequesterLogin { get; set; }  // Kept

        // Date Related
        public DateTime CreatedAt { get; set; }     // Kept
        public DateTime RequestedAt { get; set; }   // Added back

        // Chapter Data (General)
        public string ChapterNumber { get; set; }   // Kept
        public string ChapterTitle { get; set; }    // Kept

        // Existing/Current Chapter Data
        public Chapter ExistingChapterData { get; set; }    // Kept
        public string CurrentChapterNumber { get; set; }    // Added back
        public string CurrentChapterTitle { get; set; }     // Added back
        public string CurrentChapterContent { get; set; }   // Added back
        public string ExistingContent { get; set; }         // Kept

        // Proposed Chapter Data
        public Chapter ProposedChapterData { get; set; }    // Kept
        public string ProposedChapterNumber { get; set; }   // Added back
        public string ProposedChapterTitle { get; set; }    // Added back
        public string ProposedChapterContent { get; set; }  // Added back
        public string ProposedContent { get; set; }         // Kept

        // Display Properties (Calculated)
        public string RequestTypeFriendlyName => GetFriendlyRequestTypeName(RequestType, "friendly");
        public string RequestActionDisplay => GetFriendlyRequestTypeName(RequestType, "action"); // Added back

        // Helper method for display names.
        // The 'context' parameter is illustrative; if strings are identical, it's not needed.
        // For now, using the existing helper from NovelModerationRequestViewModel as a reference.
        // The actual string values might need adjustment based on specific UI requirements.
        private static string GetFriendlyRequestTypeName(ModerationRequestType requestType, string context = "friendly") // context can be "friendly" or "action"
        {
            // If "action" display needs to be shorter, e.g., "Добавление" vs "Добавление главы"
            // This is a placeholder, real logic might differ if strings need to be distinct.
            // For now, assume they might be the same for simplicity of this step,
            // focusing on having the property available.
            switch (requestType)
            {
                case ModerationRequestType.AddNovel:
                    return context == "action" ? "Добавление (Новелла)" : "Добавление новеллы";
                case ModerationRequestType.EditNovel:
                    return context == "action" ? "Редактирование (Новелла)" : "Редактирование новеллы";
                case ModerationRequestType.DeleteNovel:
                    return context == "action" ? "Удаление (Новелла)" : "Удаление новеллы";
                case ModerationRequestType.AddChapter:
                    return context == "action" ? "Добавление (Глава)" : "Добавление главы";
                case ModerationRequestType.EditChapter:
                    return context == "action" ? "Редактирование (Глава)" : "Редактирование главы";
                case ModerationRequestType.DeleteChapter:
                    return context == "action" ? "Удаление (Глава)" : "Удаление главы";
                default:
                    return requestType.ToString();
            }
        }
    }

    public class AdminUsersViewModel
    {
        public List<User> Users { get; set; }
        public string SearchTerm { get; set; } // Для хранения текущего поискового запроса
        public List<UserRole> AllRoles { get; set; } // Для выпадающего списка ролей

        public AdminUsersViewModel()
        {
            Users = new List<User>();
            AllRoles = Enum.GetValues(typeof(UserRole)).Cast<UserRole>().ToList();
        }
    }

    public class ChapterPreviewViewModel
    {
        public string NovelTitle { get; set; }
        public string ChapterFullTitle { get; set; }
        public string RenderedContent { get; set; }
        // public int RequestId { get; set; } // Optional: if needed in the view for any reason
        // public ModerationRequestType RequestType { get; set; } // Optional
    }
}
