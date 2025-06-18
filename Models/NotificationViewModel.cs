using BulbaLib.Models; // For Notification model

namespace BulbaLib.Models // Or BulbaLib.Models.ViewModels
{
    public class NotificationViewModel
    {
        public Notification Notification { get; set; }
        public string RelatedItemTitle { get; set; }
        public string RelatedItemUrl { get; set; }
        // Could add a friendly timestamp string here too, e.g., "5 minutes ago"
        public string TimeAgo { get; set; }
    }
}
