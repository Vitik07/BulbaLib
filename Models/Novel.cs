using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Models
{
    public class Novel
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public byte[] Image { get; set; }
        public string Genres { get; set; }
        public string Tags { get; set; }
    }
}