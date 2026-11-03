namespace AppDiemDanh.DTOs;

/// <summary>Standard error payload documented for API consumers. Existing legacy endpoints may still return their historical error shape.</summary>
public class ApiErrorResponse
{
    public string Code { get; set; } = "API_ERROR";
    public string Message { get; set; } = string.Empty;
    public IDictionary<string, string[]>? Errors { get; set; }
    public string? TraceId { get; set; }
}

public class EmployeeMessageResponse
{
    public int EmployeeId { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class AttendanceActionResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;
    public double FaceConfidence { get; set; }
}

public class BranchStatusResponse
{
    public int Id { get; set; }
    public bool IsActive { get; set; }
}

public class BranchWifiStatusResponse
{
    public int Id { get; set; }
    public bool IsActive { get; set; }
}

public class PayrollFinalizeResponse
{
    public string Message { get; set; } = string.Empty;
    public int EmployeeCount { get; set; }
}

public class MonthlyPayrollSummaryResponse
{
    public IReadOnlyList<PayslipResponse> Items { get; set; } = Array.Empty<PayslipResponse>();
}

public class MonthlyAttendanceSummaryResponse
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int TotalWorkDays { get; set; }
    public int TotalLateMinutes { get; set; }
    public int TotalEarlyLeaveMinutes { get; set; }
    public double TotalWorkedHours { get; set; }
    public int TotalApprovedLeaveDays { get; set; }
}

public class MessageResponse
{
    public string Message { get; set; } = string.Empty;
}

public class ApiMessageResponse
{
    public bool Success { get; set; } = true;
    public string Message { get; set; } = string.Empty;
}

public class CountResponse
{
    public int Count { get; set; }
}

public class PagedNotificationResponse
{
    public IReadOnlyList<NotificationLogDto> Items { get; set; } = Array.Empty<NotificationLogDto>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

public class EmployeeListItemResponse
{
    public int Id { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public bool HasBoundDevice { get; set; }
    public bool HasFaceRegistered { get; set; }
    public bool IsActive { get; set; }
}

public class EmployeeListResponse
{
    public IReadOnlyList<EmployeeListItemResponse> Data { get; set; } = Array.Empty<EmployeeListItemResponse>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

public class DashboardOverviewResponse
{
    public int TotalEmployees { get; set; }
    public int ActiveEmployees { get; set; }
    public int InactiveEmployees { get; set; }
    public int ActivatedEmployees { get; set; }
    public int NotActivatedEmployees { get; set; }
    public int PresentToday { get; set; }
    public int AbsentToday { get; set; }
    public int LateToday { get; set; }
    public int EarlyLeaveToday { get; set; }
    public int OnLeaveToday { get; set; }
    public int CheckedInButNotCheckedOut { get; set; }
    public int PendingLeaveRequests { get; set; }
    public int PendingAttendanceExplanations { get; set; }
    public int PendingOvertimeRequests { get; set; }
    public int PendingSalaryAdvanceRequests { get; set; }
}

public class DashboardAttendanceSummaryResponse
{
    public int TotalEmployees { get; set; }
    public int CheckedIn { get; set; }
    public int CheckedOut { get; set; }
    public int Absent { get; set; }
    public int Late { get; set; }
    public int EarlyLeave { get; set; }
    public int MissingCheckIn { get; set; }
    public int MissingCheckOut { get; set; }
    public int TotalLateMinutes { get; set; }
    public int TotalEarlyLeaveMinutes { get; set; }
}

public class DashboardPendingCountsResponse
{
    public int PendingLeave { get; set; }
    public int PendingAttendanceExplanation { get; set; }
    public int PendingOvertime { get; set; }
    public int PendingSalaryAdvance { get; set; }
}

public class TodaySummaryResponse
{
    public int TotalEmployees { get; set; }
    public int CheckedInCount { get; set; }
    public int NotCheckedInCount { get; set; }
    public int LateCount { get; set; }
    public int PendingLeaveRequests { get; set; }
}

public class BranchListItemResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class BranchListResponse
{
    public bool Success { get; set; } = true;
    public IReadOnlyList<BranchListItemResponse> Data { get; set; } = Array.Empty<BranchListItemResponse>();
}

public class IdMessageResponse
{
    public long Id { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class StatusMessageResponse
{
    public string Message { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class LeaveRequestCreatedResponse
{
    public string Message { get; set; } = string.Empty;
    public long Id { get; set; }
    public bool IsPaidLeave { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class ShiftAssignmentResponse
{
    public long Id { get; set; }
    public int ShiftId { get; set; }
    public DateTime EffectiveFromDate { get; set; }
    public DateTime? EffectiveToDate { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class OvertimeResponse
{
    public long Id { get; set; }
    public int EmployeeId { get; set; }
    public DateTime WorkDate { get; set; }
    public double Hours { get; set; }
    public double Multiplier { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class SalaryAdvanceResponse
{
    public long Id { get; set; }
    public int EmployeeId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ApprovedById { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
}
