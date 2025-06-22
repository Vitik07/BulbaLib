namespace BulbaLib.Models
{
    public class ChapterViewModel
    {
        public Chapter Chapter { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool IsBookmarked { get; set; } // Добавлено для отслеживания закладок
    }
}
