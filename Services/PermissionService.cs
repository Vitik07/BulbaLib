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

        public bool CanCreateNovel(User currentUser)
        {
            if (currentUser == null) return false;
            return currentUser.Role == UserRole.Admin || currentUser.Role == UserRole.Author;
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
            if (currentUser.Role == UserRole.Admin) return true;
            // Non-admin users (Authors) can edit if they are the creator.
            return novel.CreatorId == currentUser.Id;
        }

        public bool CanDeleteNovel(User currentUser, Novel novel)
        {
            if (currentUser == null || novel == null) return false;
            if (currentUser.Role == UserRole.Admin) return true;
            // Non-admin users (Authors) can delete if they are the creator.
            return novel.CreatorId == currentUser.Id;
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

        public bool CanCreateChapter(User currentUser, Novel novel) // Changed from novelId to Novel object for consistency
        {
            if (currentUser == null || novel == null) return false;
            if (currentUser.Role == UserRole.Admin && CanAddChapterDirectly(currentUser)) return true;
            if (currentUser.Role == UserRole.Translator && CanSubmitChapterForModeration(currentUser, novel)) return true;
            return false;
        }

        // For CanEditChapter and CanDeleteChapter, we need to ensure the Translator is linked to the Novel.
        // The Chapter model itself doesn't have a direct user link.
        public bool CanEditChapter(User currentUser, Chapter chapter, Novel novel)
        {
            if (currentUser == null || chapter == null || novel == null) return false;
            if (currentUser.Role == UserRole.Admin) return true;
            if (currentUser.Role == UserRole.Translator)
            {
                return chapter.CreatorId == currentUser.Id;
            }
            return false;
        }

        public bool CanDeleteChapter(User currentUser, Chapter chapter, Novel novel)
        {
            if (currentUser == null || chapter == null || novel == null) return false;
            if (currentUser.Role == UserRole.Admin) return true;
            if (currentUser.Role == UserRole.Translator)
            {
                return chapter.CreatorId == currentUser.Id;
            }
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
