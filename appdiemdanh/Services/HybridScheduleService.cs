using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Services;

public interface IHybridScheduleService
{
    Task<RosterResponse> ResolveAsync(RosterQuery query, CancellationToken cancellationToken = default);
}

public sealed class HybridScheduleService : IHybridScheduleService
{
    private readonly AppDbContext _db;
    public HybridScheduleService(AppDbContext db) => _db = db;

    public async Task<RosterResponse> ResolveAsync(RosterQuery query, CancellationToken cancellationToken = default)
    {
        var from = query.From.Date;
        var to = query.To.Date;
        if (from > to || (to - from).TotalDays > 366) throw new ArgumentException("Khoảng ngày không hợp lệ.");

        var employeesQuery = _db.Employees.AsNoTracking().Where(e => e.IsActive && (!query.DepartmentId.HasValue || e.DepartmentId == query.DepartmentId));
        if (query.WorkGroupId.HasValue)
            employeesQuery = employeesQuery.Where(e => _db.GroupMembers.Any(m => m.EmployeeId == e.Id && m.WorkGroupId == query.WorkGroupId && m.EffectiveDate <= to && (m.EndDate == null || m.EndDate >= from)));
        var employees = await employeesQuery.OrderBy(e => e.EmployeeCode).ToListAsync(cancellationToken);
        var employeeIds = employees.Select(e => e.Id).ToArray();
        var legacyGroups = await _db.GroupMembers.AsNoTracking().Include(x => x.WorkGroup).Where(x => employeeIds.Contains(x.EmployeeId) && x.EffectiveDate <= to && (x.EndDate == null || x.EndDate >= from)).ToListAsync(cancellationToken);
        var shiftGroupMembers = await _db.ShiftGroupEmployees.AsNoTracking().Include(x => x.ShiftGroup).Where(x => employeeIds.Contains(x.EmployeeId) && x.ShiftGroup!.IsActive).ToListAsync(cancellationToken);
        var workGroups = await _db.WorkGroups.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var shiftWorkGroupIds = shiftGroupMembers.Select(x => workGroups.FirstOrDefault(g => g.Code == x.ShiftGroup!.Code)?.Id ?? 0).Where(x => x > 0);
        var groupIds = legacyGroups.Select(x => x.WorkGroupId).Concat(shiftWorkGroupIds).Distinct().ToArray();
        var schedules = await _db.GroupSchedules.AsNoTracking().Include(x => x.Shift).Where(x => groupIds.Contains(x.WorkGroupId) && x.WorkDate >= from && x.WorkDate <= to).ToListAsync(cancellationToken);
        var exceptions = await _db.EmployeeShiftExceptions.AsNoTracking().Include(x => x.OverrideShift).Where(x => employeeIds.Contains(x.EmployeeId) && x.WorkDate >= from && x.WorkDate <= to).ToListAsync(cancellationToken);
        var defaults = await _db.EmployeeShifts.AsNoTracking().Include(x => x.Shift).Where(x => employeeIds.Contains(x.EmployeeId) && x.EffectiveFromDate <= to && (x.EffectiveToDate == null || x.EffectiveToDate >= from)).ToListAsync(cancellationToken);

        var days = Enumerable.Range(0, (to - from).Days + 1).Select(i => from.AddDays(i)).ToArray();
        var result = employees.Select(employee =>
        {
            var employeeShiftGroups = shiftGroupMembers.Where(x => x.EmployeeId == employee.Id).ToList();
            var employeeGroups = employeeShiftGroups.Count > 0
                ? employeeShiftGroups.Select(x => new RosterMembership
                {
                    WorkGroupId = workGroups.FirstOrDefault(g => g.Code == x.ShiftGroup!.Code)?.Id ?? 0,
                    GroupName = x.ShiftGroup!.Name,
                    EffectiveDate = from,
                    EndDate = to
                }).ToList()
                : legacyGroups.Where(x => x.EmployeeId == employee.Id).Select(x => new RosterMembership
                {
                    WorkGroupId = x.WorkGroupId,
                    GroupName = x.WorkGroup?.Name,
                    EffectiveDate = x.EffectiveDate,
                    EndDate = x.EndDate,
                    Id = x.Id
                }).ToList();
            var resolvedDays = days.Select(day =>
            {
                var exception = exceptions.FirstOrDefault(x => x.EmployeeId == employee.Id && x.WorkDate == day);
                var membership = employeeGroups.Where(x => x.EffectiveDate.Date <= day && (!x.EndDate.HasValue || x.EndDate.Value.Date >= day)).OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id).FirstOrDefault();
                var groupSchedule = membership == null || membership.WorkGroupId == 0 ? null : schedules.FirstOrDefault(x => x.WorkGroupId == membership.WorkGroupId && x.WorkDate == day);
                var defaultShift = defaults.Where(x => x.EmployeeId == employee.Id && x.EffectiveFromDate.Date <= day && (!x.EffectiveToDate.HasValue || x.EffectiveToDate.Value.Date >= day)).OrderByDescending(x => x.EffectiveFromDate).ThenByDescending(x => x.Id).FirstOrDefault();
                var shift = exception?.OverrideShift ?? defaultShift?.Shift ?? groupSchedule?.Shift;
                var selectedIndividualShiftId = exception?.OverrideShiftId ?? defaultShift?.ShiftId;
                var hasDifferentIndividualShift = selectedIndividualShiftId.HasValue
                    && (groupSchedule == null || selectedIndividualShiftId.Value != groupSchedule.ShiftId);
                var source = hasDifferentIndividualShift
                    ? exception != null ? "Exception" : "Employee"
                    : groupSchedule != null ? "Group" : "Default";
                var assignmentId = source == "Exception" ? exception?.Id : source == "Employee" ? defaultShift?.Id : groupSchedule?.Id;
                return new ResolvedShiftDto { AssignmentId = assignmentId, WorkDate = day, ShiftId = shift?.Id, ShiftCode = shift?.ShiftCode, ShiftName = shift?.ShiftName, StartTime = shift?.StartTime, EndTime = shift?.EndTime, Source = source, ExceptionId = source == "Exception" ? exception?.Id : null };
            }).ToList();
            return new RosterEmployeeDto { EmployeeId = employee.Id, EmployeeCode = employee.EmployeeCode, EmployeeName = employee.FullName, WorkGroupName = employeeGroups.OrderByDescending(x => x.EffectiveDate).FirstOrDefault()?.GroupName, Days = resolvedDays };
        }).ToList();
        return new RosterResponse { From = from, To = to, Employees = result };
    }

    private sealed class RosterMembership
    {
        public long Id { get; init; }
        public int WorkGroupId { get; init; }
        public string? GroupName { get; init; }
        public DateTime EffectiveDate { get; init; }
        public DateTime? EndDate { get; init; }
    }
}
