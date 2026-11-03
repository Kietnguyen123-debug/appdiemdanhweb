using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AppDiemDanh.Controllers;

[ApiController]
[Authorize(Roles = "Admin,HR,Manager")]
[Route("api/v1/schedules")]
public sealed class HybridScheduleController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHybridScheduleService _resolver;
    public HybridScheduleController(AppDbContext db, IHybridScheduleService resolver) { _db = db; _resolver = resolver; }

    [HttpPost("groups/bulk-assign")]
    public async Task<IActionResult> BulkAssign([FromBody] GroupBulkAssignRequest request, CancellationToken cancellationToken)
    {
        if (request.From.Date > request.To.Date || (request.To.Date - request.From.Date).TotalDays > 366) return BadRequest(new { message = "Khoảng ngày không hợp lệ." });
        if (!await _db.WorkGroups.AnyAsync(x => x.Id == request.WorkGroupId && x.IsActive, cancellationToken)) return NotFound(new { message = "Không tìm thấy nhóm làm việc." });
        var days = request.Days?.Select(x => (Date: x.WorkDate.Date, ShiftId: x.ShiftId)).ToList() ?? Enumerable.Range(0, (request.To.Date - request.From.Date).Days + 1).Select(i => (request.From.Date.AddDays(i), request.ShiftId)).ToList();
        if (days.Count == 0 || days.Any(x => x.Date < request.From.Date || x.Date > request.To.Date)) return BadRequest(new { message = "Danh sách ngày không hợp lệ." });
        var shiftIds = days.Select(x => x.ShiftId).Distinct().ToArray();
        if (await _db.Shifts.CountAsync(x => shiftIds.Contains(x.Id) && x.IsActive, cancellationToken) != shiftIds.Length) return BadRequest(new { message = "Danh sách ca có ca không tồn tại hoặc đã khóa." });
        var dates = days.Select(x => x.Date).ToArray();
        var existing = await _db.GroupSchedules.Where(x => x.WorkGroupId == request.WorkGroupId && dates.Contains(x.WorkDate)).ToListAsync(cancellationToken);
        var adminId = GetAdminId();
        foreach (var item in days)
        {
            var schedule = existing.FirstOrDefault(x => x.WorkDate == item.Date);
            if (schedule == null) _db.GroupSchedules.Add(new GroupSchedule { WorkGroupId = request.WorkGroupId, WorkDate = item.Date, ShiftId = item.ShiftId, CreatedBy = adminId });
            else { schedule.ShiftId = item.ShiftId; schedule.CreatedBy = adminId; schedule.CreatedAt = DateTime.UtcNow; }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { assigned = days.Count, workGroupId = request.WorkGroupId });
    }

    [HttpPost("employees/override")]
    public async Task<IActionResult> Override([FromBody] EmployeeOverrideRequest request, CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0) return BadRequest(new { message = "Danh sách ngoại lệ không được rỗng." });
        var employeeIds = request.Items.Select(x => x.EmployeeId).Distinct().ToArray();
        var shiftIds = request.Items.Select(x => x.OverrideShiftId).Distinct().ToArray();
        if (await _db.Employees.CountAsync(x => employeeIds.Contains(x.Id) && x.IsActive, cancellationToken) != employeeIds.Length || await _db.Shifts.CountAsync(x => shiftIds.Contains(x.Id) && x.IsActive, cancellationToken) != shiftIds.Length) return BadRequest(new { message = "Nhân viên hoặc ca không tồn tại/không hoạt động." });
        var dates = request.Items.Select(x => x.WorkDate.Date).ToArray();
        var existing = await _db.EmployeeShiftExceptions.Where(x => employeeIds.Contains(x.EmployeeId) && dates.Contains(x.WorkDate)).ToListAsync(cancellationToken);
        var adminId = GetAdminId();
        foreach (var item in request.Items)
        {
            var exception = existing.FirstOrDefault(x => x.EmployeeId == item.EmployeeId && x.WorkDate == item.WorkDate.Date);
            if (exception == null) _db.EmployeeShiftExceptions.Add(new EmployeeShiftException { EmployeeId = item.EmployeeId, WorkDate = item.WorkDate.Date, OverrideShiftId = item.OverrideShiftId, Reason = item.Reason.Trim(), CreatedBy = adminId });
            else { exception.OverrideShiftId = item.OverrideShiftId; exception.Reason = item.Reason.Trim(); exception.CreatedBy = adminId; exception.UpdatedAt = DateTime.UtcNow; }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { overridden = request.Items.Count });
    }

    [HttpDelete("employees/override/{id:long}")]
    public async Task<IActionResult> DeleteOverride(long id, CancellationToken cancellationToken)
    {
        var exception = await _db.EmployeeShiftExceptions.FindAsync([id], cancellationToken);
        if (exception == null) return NotFound();
        _db.EmployeeShiftExceptions.Remove(exception);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("roster")]
    [ProducesResponseType(typeof(RosterResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Roster([FromQuery] RosterQuery query, CancellationToken cancellationToken)
    {
        if (query.From == default || query.To == default || query.From.Date > query.To.Date) return BadRequest(new { message = "Cần truyền from/to hợp lệ." });
        try { return Ok(await _resolver.ResolveAsync(query, cancellationToken)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    private int GetAdminId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : throw new UnauthorizedAccessException();
}
