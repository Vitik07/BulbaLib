using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Models
{
    public class Bookmark
    {
        public int UserId { get; set; }
        public int NovelId { get; set; }
        public int ChapterId { get; set; }
    }
}