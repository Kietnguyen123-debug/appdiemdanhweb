using System.ComponentModel.DataAnnotations;

namespace AppDiemDanh.DTOs;

public class PagedResponse<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

public class DepartmentResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int EmployeeCount { get; set; }
}

public class DepartmentListItemResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class DepartmentListResponse
{
    public bool Success { get; set; }
    public IReadOnlyList<DepartmentListItemResponse> Data { get; set; } = Array.Empty<DepartmentListItemResponse>();
}

public class DepartmentRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}

public class CreateEmployeeProfileRequest
{
    [Required, StringLength(50, MinimumLength = 1)] public string EmployeeCode { get; set; } = string.Empty;
    [Required, StringLength(200, MinimumLength = 1)] public string FullName { get; set; } = string.Empty;
    [Range(1, int.MaxValue)] public int BranchId { get; set; }
    public int? DepartmentId { get; set; }
    [Required] public string Role { get; set; } = "Employee";
    [EmailAddress, StringLength(320)] public string? Email { get; set; }
    [Range(typeof(decimal), "0", "1000000000000")] public decimal BaseSalary { get; set; }
    [Range(typeof(decimal), "0", "1000000000000")] public decimal Allowance { get; set; }
    [Range(typeof(decimal), "0", "1000000000")] public decimal PenaltyPerLateMinute { get; set; } = 2000;
    [Range(0, 20)] public int DependentCount { get; set; }
    public decimal? SocialInsuranceBase { get; set; }
    [Range(1, int.MaxValue)] public int TotalAnnualLeave { get; set; } = 12;
}

public class AttendanceReportDto
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int TotalWorkDays { get; set; }
    public int PresentDays { get; set; }
    public int AbsentDays { get; set; }
    public int ApprovedLeaveDays { get; set; }
    public int TotalLateMinutes { get; set; }
    public int TotalEarlyLeaveMinutes { get; set; }
    public double TotalWorkedHours { get; set; }
    public int MissingCheckInCount { get; set; }
    public int MissingCheckOutCount { get; set; }
}

public class LeaveReportDto
{
    public string Status { get; set; } = string.Empty;
    public int RequestCount { get; set; }
    public int LeaveDays { get; set; }
}

public class OvertimeReportDto
{
    public string Status { get; set; } = string.Empty;
    public double TotalHours { get; set; }
    public int RequestCount { get; set; }
}

public class EmployeeDetailResponse
{
    public int Id { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int BranchId { get; set; }
    public int? DepartmentId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsActivated { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool HasBoundDevice { get; set; }
    public bool HasFaceRegistered { get; set; }
    public decimal BaseSalary { get; set; }
    public decimal Allowance { get; set; }
    public decimal PenaltyPerLateMinute { get; set; }
    public int DependentCount { get; set; }
    public decimal? SocialInsuranceBase { get; set; }
    public int TotalAnnualLeave { get; set; }
    public int UsedAnnualLeave { get; set; }
    public int RemainingAnnualLeave { get; set; }
}

public class AdminAttendanceDto
{
    public long AttendanceId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public DateTime WorkDate { get; set; }
    public DateTime CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public class AdjustAttendanceRequest
{
    public DateTime? CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    [Required, StringLength(1000, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;
}

public class BranchResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int EmployeeCount { get; set; }
    public int PublicIpCount { get; set; }
    public int WifiCount { get; set; }
}

public class BranchRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}

public class BranchIpDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string PublicIp { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class BranchIpRequest
{
    [Required]
    public string PublicIp { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class BranchWifiDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string Ssid { get; set; } = string.Empty;
    public string Bssid { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class BranchWifiRequest
{
    [Required, StringLength(32, MinimumLength = 1)]
    public string Ssid { get; set; } = string.Empty;
    [Required]
    public string Bssid { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class AdminLeaveDto
{
    public long Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string LeaveType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ReviewerId { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminOvertimeDto
{
    public long Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public DateTime WorkDate { get; set; }
    public double Hours { get; set; }
    public double Multiplier { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class AdminSalaryAdvanceDto
{
    public long Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ApprovedById { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminShiftAssignmentDto
{
    public long Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public int ShiftId { get; set; }
    public string ShiftCode { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public DateTime EffectiveFromDate { get; set; }
    public DateTime? EffectiveToDate { get; set; }
}

public class UpdateShiftAssignmentRequest
{
    [Range(1, int.MaxValue)] public int ShiftId { get; set; }
    [Required] public DateTime EffectiveFromDate { get; set; }
    public DateTime? EffectiveToDate { get; set; }
}
