using System.ComponentModel.DataAnnotations;

namespace AppDiemDanh.DTOs
{
    public class CreateLeaveRequest
    {
        [Required]
        public DateTime StartDate { get; set; }
        [Required]
        public DateTime EndDate { get; set; }
        [Required]
        public string LeaveType { get; set; } = "ANNUAL";
        [Required, StringLength(1000, MinimumLength = 1)]
        public string Reason { get; set; } = string.Empty;
    }

    public class LeaveBalanceDto
    {
        public int TotalAnnualLeave { get; set; }
        public int UsedAnnualLeave { get; set; }
        public int RemainingAnnualLeave { get; set; }
    }

    public class LeaveApprovalResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public string? Status { get; init; }
    }
}
