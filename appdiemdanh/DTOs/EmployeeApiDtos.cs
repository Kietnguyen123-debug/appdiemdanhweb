using System.ComponentModel.DataAnnotations;

namespace AppDiemDanh.DTOs;

public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 6)]
    public string NewPassword { get; set; } = string.Empty;
}

public class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

public class AuthTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class EmployeeMeResponse
{
    public int Id { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string? Email { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool IsActivated { get; set; }
}

public class AttendanceItemDto
{
    public long Id { get; set; }
    public DateTime WorkDate { get; set; }
    public DateTime CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }
    public int WorkedMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public class AttendanceSummaryDto
{
    public int TotalDays { get; set; }
    public int PresentDays { get; set; }
    public int TotalWorkedMinutes { get; set; }
    public int TotalLateMinutes { get; set; }
    public int TotalEarlyLeaveMinutes { get; set; }
}

public class EmployeeLeaveDto
{
    public long Id { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string LeaveType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsPaidLeave { get; set; }
    public string? RejectReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class EmployeeOvertimeDto
{
    public long Id { get; set; }
    public DateTime WorkDate { get; set; }
    public double Hours { get; set; }
    public double Multiplier { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class EmployeeSalaryAdvanceDto
{
    public long Id { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ReviewNote { get; set; }
}

public class EmployeeShiftScheduleDto
{
    public long AssignmentId { get; set; }
    public DateTime EffectiveFromDate { get; set; }
    public DateTime? EffectiveToDate { get; set; }
    public ShiftDto Shift { get; set; } = new();
}

public class PayrollHistoryItemDto
{
    public long Id { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal NetSalary { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CancelRequestResponse
{
    public long Id { get; set; }
    public string Status { get; set; } = string.Empty;
}
