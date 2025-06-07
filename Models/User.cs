using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Login { get; set; }
        public string Password { get; set; }
        public string Role { get; set; }
        public byte[] Avatar { get; set; }
    }
}