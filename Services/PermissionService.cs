using BulbaLib.Models; // Assuming User, Novel, Chapter models are accessible via this
// If User model is still defined in MySqlService.cs, this using might need adjustment
// or the User model should be moved to Models/User.cs

namespace BulbaLib.Services
{
    public class PermissionService
    {
        private readonly MySqlService _mySqlService;

        public PermissionService(MySqlService mySqlService)
        {
            _mySqlService = mySqlService;
        }

        public bool CanManageUsers(User currentUser)
        {
            return currentUser != null && currentUser.Role == "Admin";
        }

        public bool CanBlockUser(User currentUser, User targetUser)
        {
            return currentUser != null && targetUser != null &&
                   currentUser.Role == "Admin" && currentUser.Id != targetUser.Id;
        }

        public bool CanChangeUserRole(User currentUser, User targetUser)
        {
            return currentUser != null && targetUser != null &&
                   currentUser.Role == "Admin" && currentUser.Id != targetUser.Id;
        }

        public bool CanSubmitNovelForModeration(User currentUser)
        {
            return currentUser != null && currentUser.Role == "Author";
        }

        public bool CanAddNovelDirectly(User currentUser)
        {
            return currentUser != null && currentUser.Role == "Admin";
        }

        public bool CanEditNovel(User currentUser, Novel novel)
        {
            if (currentUser == null || novel == null) return false;
            if (currentUser.Role == "Admin") return true;
            return currentUser.Role == "Author" && novel.AuthorId == currentUser.Id;
        }

        public bool CanDeleteNovel(User currentUser, Novel novel)
        {
            return CanEditNovel(currentUser, novel); // Same logic
        }

        public bool CanSubmitChapterForModeration(User currentUser, Novel novel)
        {
            // Translator can submit chapters for a novel they are assigned to
            return currentUser != null && novel != null &&
                   currentUser.Role == "Translator" &&
                   (novel.TranslatorId ?? string.Empty).Contains(currentUser.Id.ToString());
        }

        public bool CanAddChapterDirectly(User currentUser)
        {
            return currentUser != null && currentUser.Role == "Admin";
        }

        // For CanEditChapter and CanDeleteChapter, we need to ensure the Translator is linked to the Novel.
        // The Chapter model itself doesn't have a direct user link.
        public bool CanEditChapter(User currentUser, Chapter chapter, Novel novel)
        {
            if (currentUser == null || chapter == null || novel == null) return false;
            if (currentUser.Role == "Admin") return true;
            // Assuming TranslatorId in Novel can be a list of IDs or a single ID.
            // For simplicity, check if the current user's ID is part of the TranslatorId string.
            // This might need refinement based on how TranslatorId is actually stored and formatted.
            return currentUser.Role == "Translator" &&
                   (novel.TranslatorId ?? string.Empty).Contains(currentUser.Id.ToString());
        }

        public bool CanDeleteChapter(User currentUser, Chapter chapter, Novel novel)
        {
            return CanEditChapter(currentUser, chapter, novel); // Same logic
        }

        public bool CanViewAdminPanel(User currentUser)
        {
            return currentUser != null && currentUser.Role == "Admin";
        }

        public bool CanModerateNovelRequests(User currentUser)
        {
            return currentUser != null && currentUser.Role == "Admin";
        }

        public bool CanModerateChapterRequests(User currentUser)
        {
            return currentUser != null && currentUser.Role == "Admin";
        }
    }
}
