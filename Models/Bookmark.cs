namespace BulbaLib.Models
{
    public class Bookmark
    {
        public int UserId { get; set; }
        public int NovelId { get; set; }
        public int ChapterId { get; set; }
        public long Date { get; set; }
    }
}