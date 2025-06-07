using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Models
{
    public class Chapter
    {
        public int Id { get; set; }
        public int NovelId { get; set; }
        public int Number { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public long Date { get; set; }
    }
}