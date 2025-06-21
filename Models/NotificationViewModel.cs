using System;

namespace BulbaLib.Models
{
    public class NotificationViewModel
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Type { get; set; } // Changed from NotificationType enum to string for display
        public string Message { get; set; }
        public string Link { get; set; }
        public DateTime DateSent { get; set; }
        // Optional: Add a property for "TimeAgo" if you prefer to calculate it in the backend.
        // public string TimeAgo { get; set; } 
    }
}
