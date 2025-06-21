using Microsoft.AspNetCore.Mvc;

namespace BulbaLib.Models
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public string? Message { get; set; } // Added for custom error messages

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}