using System.ComponentModel.DataAnnotations;

namespace BulbaLib.Models
{
    public enum NovelStatus
    {
        [Display(Name = "На утверждении")]
        PendingApproval,
        [Display(Name = "Утверждено")]
        Approved,
        [Display(Name = "Отклонено")]
        Rejected,
        [Display(Name = "Черновик")]
        Draft
    }
}
