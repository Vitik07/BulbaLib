using BulbaLib.Models; // Assuming User, Novel, Chapter models are accessible via this
// If User model is still defined in MySqlService.cs, this using might need adjustment
// or the User model should be moved to Models/User.cs
using System.Linq;

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
            return currentUser != null && currentUser.Role == UserRole.Admin;
        }

        public bool CanBlockUser(User currentUser, User targetUser)
        {
            return currentUser != null && targetUser != null &&
                   currentUser.Role == UserRole.Admin && currentUser.Id != targetUser.Id;
        }

        public bool CanChangeUserRole(User currentUser, User targetUser)
        {
            return currentUser != null && targetUser != null &&
                   currentUser.Role == UserRole.Admin && currentUser.Id != targetUser.Id;
        }

        public bool CanSubmitNovelForModeration(User currentUser)
        {
            return currentUser != null && currentUser.Role == UserRole.Author;
        }

        public bool CanAddNovelDirectly(User currentUser)
        {
            return currentUser != null && currentUser.Role == UserRole.Admin;
        }

        public bool CanEditNovel(User currentUser, Novel novel)
        {
            if (currentUser == null || novel == null) return false;
            // Admins can directly edit any novel.
            if (currentUser.Role == UserRole.Admin) return true;
            // Authors cannot directly edit novels; they must submit for moderation.
            // The check for novel.AuthorId == currentUser.Id is done in the controller before creating a moderation request.
            return false;
        }

        public bool CanDeleteNovel(User currentUser, Novel novel)
        {
            if (currentUser == null || novel == null) return false;
            // Admins can directly delete any novel.
            if (currentUser.Role == UserRole.Admin) return true;
            // Authors cannot directly delete novels; they must submit for moderation.
            return false;
        }

        public bool CanSubmitChapterForModeration(User currentUser, Novel novel)
        {
            // Translator can submit chapters for any novel (novel parameter kept for potential future novel-specific checks)
            return currentUser != null && currentUser.Role == UserRole.Translator && novel != null;
        }

        public bool CanAddChapterDirectly(User currentUser)
        {
            return currentUser != null && currentUser.Role == UserRole.Admin;
        }

        // For CanEditChapter and CanDeleteChapter, we need to ensure the Translator is linked to the Novel.
        // The Chapter model itself doesn't have a direct user link.
        public bool CanEditChapter(User currentUser, Chapter chapter, Novel novel)
        {
            if (currentUser == null || chapter == null || novel == null) return false;
            // Admins can directly edit any chapter.
            if (currentUser.Role == UserRole.Admin) return true;
            // Translators cannot directly edit chapters; they must submit for moderation.
            // The check for chapter.CreatorId == currentUser.Id and IsTranslatorAssignedToNovel is done 
            // in the controller before creating a moderation request.
            return false;
        }

        public bool CanDeleteChapter(User currentUser, Chapter chapter, Novel novel)
        {
            if (currentUser == null || chapter == null || novel == null) return false;
            // Admins can directly delete any chapter.
            if (currentUser.Role == UserRole.Admin) return true;
            // Translators cannot directly delete chapters; they must submit for moderation.
            return false;
        }

        public bool CanViewAdminPanel(User currentUser)
        {
            return currentUser != null && currentUser.Role == UserRole.Admin;
        }

        public bool CanModerateNovelRequests(User currentUser)
        {
            return currentUser != null && currentUser.Role == UserRole.Admin;
        }

        public bool CanModerateChapterRequests(User currentUser)
        {
            return currentUser != null && currentUser.Role == UserRole.Admin;
        }
    }
}
