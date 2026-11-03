using System.ComponentModel.DataAnnotations;

namespace AppDiemDanh.DTOs
{
    public class SubmitAttendanceExplanationRequest
    {
        [Required]
        public DateTime TargetDate { get; set; }
        [Required, StringLength(2000, MinimumLength = 1)]
        public string ExplanationReason { get; set; } = string.Empty;
        public DateTime? MissingCheckInTime { get; set; }
        public DateTime? MissingCheckOutTime { get; set; }
    }

    public class ReviewAttendanceExplanationRequest
    {
        [Required]
        public string Status { get; set; } = string.Empty;
        public string? ReviewNote { get; set; }
    }

    public class AttendanceExplanationResponse
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public string? EmployeeName { get; set; }
        public DateTime TargetDate { get; set; }
        public string ExplanationReason { get; set; } = string.Empty;
        public DateTime? MissingCheckInTime { get; set; }
        public DateTime? MissingCheckOutTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public int? ReviewerId { get; set; }
        public string? ReviewNote { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
