using System;
using System.ComponentModel.DataAnnotations;

namespace neworbit.Models
{
    public class ModerationRequest
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } // Or int, depending on your User Id type

        public User User { get; set; }

        public DateTime RequestDate { get; set; } = DateTime.UtcNow;

        public string Reason { get; set; }

        public bool IsApproved { get; set; } = false;

        public string? ModeratorId { get; set; } // Or int, for who reviewed it

        public User? Moderator { get; set; }

        public DateTime? ReviewDate { get; set; }
    }
}
