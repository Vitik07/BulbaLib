using BulbaLib.Models;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace BulbaLib.Services
{
    public interface ICurrentUserService
    {
        User GetCurrentUser();
        string GetCurrentUserRole();
        int? GetCurrentUserId();
    }

    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly MySqlService _mySqlService;
        private User _cachedUser; // Basic caching for the current request

        public CurrentUserService(IHttpContextAccessor httpContextAccessor, MySqlService mySqlService)
        {
            _httpContextAccessor = httpContextAccessor;
            _mySqlService = mySqlService;
        }

        public int? GetCurrentUserId()
        {
            var userIdString = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdString, out int userId))
            {
                return userId;
            }
            return null;
        }

        public User GetCurrentUser()
        {
            if (_cachedUser != null)
            {
                return _cachedUser;
            }

            var userId = GetCurrentUserId();
            if (userId.HasValue)
            {
                _cachedUser = _mySqlService.GetUser(userId.Value);
                return _cachedUser;
            }
            return null;
        }

        public string GetCurrentUserRole()
        {
            var currentUser = GetCurrentUser();
            if (currentUser == null)
            {
                return null;
            }
            return currentUser.Role.ToString();
        }
    }
}
