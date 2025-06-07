using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Models
{
    public class Favorite
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int NovelId { get; set; }
        public string Status { get; set; }
    }
}