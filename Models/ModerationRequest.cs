using System;

namespace BulbaLib.Models
{
    public class ModerationRequest
    {
        public int Id { get; set; }
        public ModerationRequestType RequestType { get; set; }
        public int UserId { get; set; } // Кто отправил запрос
        public int? NovelId { get; set; }
        public int? ChapterId { get; set; }
        public string RequestData { get; set; } // JSON с данными
        public ModerationStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? ModeratorId { get; set; } // Кто обработал
        public string ModerationComment { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
