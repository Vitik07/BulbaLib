using System.ComponentModel.DataAnnotations;

namespace BulbaLib.Models
{
    public class CreateChapterDraftRequest
    {
        [Required]
        public int NovelId { get; set; }
    }
}
