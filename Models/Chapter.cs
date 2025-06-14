using System;

namespace BulbaLib.Models
{
    public class Chapter
    {
        public int Id { get; set; }
        public int NovelId { get; set; }
        public string Number { get; set; } // было int, теперь string
        public string Title { get; set; }
        public string Content { get; set; }
        public long Date { get; set; }
        public int? CreatorId { get; set; } // Nullable int, т.к. старые главы могут его не иметь или создатель неизвестен
    }
}