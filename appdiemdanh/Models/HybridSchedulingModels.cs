namespace AppDiemDanh.Models;

public sealed class WorkGroup
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public int? DefaultShiftId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Department? Department { get; set; }
    public Shift? DefaultShift { get; set; }
    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
    public ICollection<GroupSchedule> Schedules { get; set; } = new List<GroupSchedule>();
}

public sealed class ShiftGroup
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ShiftGroupEmployee> Employees { get; set; } = new List<ShiftGroupEmployee>();
}

public sealed class ShiftGroupEmployee
{
    public int ShiftGroupId { get; set; }
    public int EmployeeId { get; set; }

    public ShiftGroup? ShiftGroup { get; set; }
    public Employee? Employee { get; set; }
}

public sealed class GroupMember
{
    public long Id { get; set; }
    public int WorkGroupId { get; set; }
    public int EmployeeId { get; set; }
    public DateTime EffectiveDate { get; set; }
    public DateTime? EndDate { get; set; }

    public WorkGroup? WorkGroup { get; set; }
    public Employee? Employee { get; set; }
}

public sealed class GroupSchedule
{
    public long Id { get; set; }
    public int WorkGroupId { get; set; }
    public DateTime WorkDate { get; set; }
    public int ShiftId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }

    public WorkGroup? WorkGroup { get; set; }
    public Shift? Shift { get; set; }
    public Employee? Creator { get; set; }
}

public sealed class EmployeeShiftException
{
    public long Id { get; set; }
    public int EmployeeId { get; set; }
    public DateTime WorkDate { get; set; }
    public int OverrideShiftId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Employee? Employee { get; set; }
    public Shift? OverrideShift { get; set; }
    public Employee? Creator { get; set; }
}
