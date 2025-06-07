using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;

namespace BulbaLib.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly SqliteService _db;
        private readonly IWebHostEnvironment _env;

        public AuthController(SqliteService db, IWebHostEnvironment env)
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

            // Загружаем дефолтный аватар
            var avatarPath = Path.Combine(_env.WebRootPath, "Resource", "default-avatar.jpg");
            var avatar = System.IO.File.Exists(avatarPath) ? System.IO.File.ReadAllBytes(avatarPath) : null;
            _db.CreateUser(req.Login, req.Password, avatar);

            return StatusCode(201, new { message = "Registration successful" });
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest req)
        {
            var user = _db.AuthenticateUser(req.Login, req.Password);
            if (user == null)
                return Unauthorized(new { error = "Invalid credentials" });

            return Ok(new
            {
                message = "Login successful",
                userId = user.Id,
                role = user.Role,
                hasAvatar = user.Avatar != null
            });
        }
    }

    // DTOs (их лучше вынести в отдельную папку Models/DTO)
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