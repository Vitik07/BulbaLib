using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BulbaLib.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly MySqlService _db;
        private readonly IWebHostEnvironment _env;

        public AuthController(MySqlService db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Login) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest(new { error = "Login and password required" });

            if (_db.UserExists(req.Login))
                return Conflict(new { error = "User already exists" });

            var avatarPath = Path.Combine(_env.WebRootPath, "Resource", "default-avatar.jpg");
            var avatar = System.IO.File.Exists(avatarPath) ? System.IO.File.ReadAllBytes(avatarPath) : null;
            _db.CreateUser(req.Login, req.Password, avatar);

            return StatusCode(201, new { message = "Registration successful" });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            var user = _db.AuthenticateUser(req.Login, req.Password);
            if (user == null)
                return Unauthorized(new { error = "Invalid credentials" });

            // Генерируем клаймы пользователя
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Login),
                new Claim(ClaimTypes.Role, user.Role ?? "User")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            // Устанавливаем куку авторизации
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            return Ok(new
            {
                message = "Login successful",
                user = new { id = user.Id, login = user.Login, role = user.Role, hasAvatar = user.Avatar != null }
            });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new { message = "Logout successful" });
        }
    }

    // DTOs
    public class RegisterRequest
    {
        public string Login { get; set; }
        public string Password { get; set; }
    }

    public class LoginRequest
    {
        public string Login { get; set; }
        public string Password { get; set; }
    }
}