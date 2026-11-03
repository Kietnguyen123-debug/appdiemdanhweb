using System.ComponentModel.DataAnnotations;

namespace AppDiemDanh.DTOs;

public sealed class GroupBulkAssignRequest
{
    [Range(1, int.MaxValue)] public int WorkGroupId { get; set; }
    [Required] public DateTime From { get; set; }
    [Required] public DateTime To { get; set; }
    [Range(1, int.MaxValue)] public int ShiftId { get; set; }
    public List<GroupScheduleDayRequest>? Days { get; set; }
}

public sealed class GroupScheduleDayRequest
{
    [Required] public DateTime WorkDate { get; set; }
    [Range(1, int.MaxValue)] public int ShiftId { get; set; }
}

public sealed class EmployeeOverrideRequest
{
    [Required, MinLength(1)] public List<EmployeeOverrideItem> Items { get; set; } = [];
}

public sealed class EmployeeOverrideItem
{
    [Range(1, int.MaxValue)] public int EmployeeId { get; set; }
    [Required] public DateTime WorkDate { get; set; }
    [Range(1, int.MaxValue)] public int OverrideShiftId { get; set; }
    [Required, StringLength(1000, MinimumLength = 2)] public string Reason { get; set; } = string.Empty;
}

public sealed class RosterQuery
{
    public int? DepartmentId { get; set; }
    public int? WorkGroupId { get; set; }
    [Required] public DateTime From { get; set; }
    [Required] public DateTime To { get; set; }
}

public sealed class RosterResponse
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public IReadOnlyList<RosterEmployeeDto> Employees { get; set; } = [];
}

public sealed class RosterEmployeeDto
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? WorkGroupName { get; set; }
    public IReadOnlyList<ResolvedShiftDto> Days { get; set; } = [];
}

public sealed class ResolvedShiftDto
{
    public long? AssignmentId { get; set; }
    public DateTime WorkDate { get; set; }
    public int? ShiftId { get; set; }
    public string? ShiftCode { get; set; }
    public string? ShiftName { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string Source { get; set; } = "Default";
    public long? ExceptionId { get; set; }
}
