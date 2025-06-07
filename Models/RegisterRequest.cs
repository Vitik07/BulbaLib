using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Models
{
    public class RegisterRequest
    {
        public string Login { get; set; }
        public string Password { get; set; }
    }
}
