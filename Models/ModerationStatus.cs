using System.ComponentModel.DataAnnotations;

namespace BulbaLib.Models
{
    public enum ModerationStatus
    {
        [Display(Name = "Ожидает")]
        Pending,
        [Display(Name = "Одобрено")]
        Approved,
        [Display(Name = "Отклонено")]
        Rejected
    }
}
