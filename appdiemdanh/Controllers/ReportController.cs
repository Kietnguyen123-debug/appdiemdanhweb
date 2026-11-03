using System;
using System.Linq;
using System.Threading.Tasks;
using AppDiemDanh.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppDiemDanh.DTOs;

namespace AppDiemDanh.Controllers
{
    [Authorize(Roles = "Admin,HR,Manager")]
    [ApiController]
    [Route("api/[controller]")]
    public class ReportController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ReportController(AppDbContext context) => _context = context;

        [HttpGet("attendance")]
        [ProducesResponseType(typeof(IEnumerable<AttendanceReportDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Attendance([FromQuery] int month, [FromQuery] int year, [FromQuery] int? branchId, [FromQuery] int? employeeId)
        {
            if (month is < 1 or > 12 || year is < 2000 or > 2100 || branchId is <= 0 || employeeId is <= 0) return BadRequest(new { message = "Tháng, năm hoặc bộ lọc không hợp lệ." });
            var start = new DateTime(year, month, 1); var end = start.AddMonths(1);
            if (!ResolveManagerBranch(ref branchId)) return Forbid();
            var employees = await _context.Employees.AsNoTracking().Where(x => x.IsActive && (!branchId.HasValue || x.BranchId == branchId) && (!employeeId.HasValue || x.Id == employeeId)).ToListAsync();
            var ids = employees.Select(x => x.Id).ToArray();
            var attendances = await _context.Attendances.AsNoTracking().Where(x => ids.Contains(x.EmployeeId) && x.WorkDate >= start && x.WorkDate < end).ToListAsync();
            var leaves = await _context.LeaveRequests.AsNoTracking().Where(x => ids.Contains(x.EmployeeId) && x.Status == "APPROVED" && x.StartDate < end && x.EndDate >= start).ToListAsync();
            return Ok(employees.Select(e => { var a = attendances.Where(x => x.EmployeeId == e.Id).ToList(); var l = leaves.Where(x => x.EmployeeId == e.Id).Sum(x => Math.Max(0, (x.EndDate.Date - x.StartDate.Date).Days + 1)); return new AttendanceReportDto { EmployeeId = e.Id, EmployeeCode = e.EmployeeCode, EmployeeName = e.FullName, TotalWorkDays = a.Count, PresentDays = a.Count(x => x.CheckInTime != default), AbsentDays = Math.Max(0, 26 - a.Count), ApprovedLeaveDays = l, TotalLateMinutes = a.Sum(x => x.LateMinutes), TotalEarlyLeaveMinutes = a.Sum(x => x.EarlyLeaveMinutes), TotalWorkedHours = Math.Round(a.Sum(x => x.WorkedMinutes) / 60d, 2), MissingCheckInCount = a.Count(x => x.CheckInTime == default), MissingCheckOutCount = a.Count(x => x.CheckInTime != default && !x.CheckOutTime.HasValue) }; }));
        }

        [HttpGet("leave")]
        [ProducesResponseType(typeof(IReadOnlyList<LeaveReportDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Leave([FromQuery] int month, [FromQuery] int year, [FromQuery] int? branchId, [FromQuery] int? employeeId, [FromQuery] string? status)
        {
            if (month is < 1 or > 12 || year is < 2000 or > 2100 || branchId is <= 0 || employeeId is <= 0) return BadRequest();
            var start = new DateTime(year, month, 1); var end = start.AddMonths(1);
            if (!ResolveManagerBranch(ref branchId)) return Forbid();
            var query = _context.LeaveRequests.AsNoTracking().Where(x => (!branchId.HasValue || x.Employee!.BranchId == branchId) && (!employeeId.HasValue || x.EmployeeId == employeeId) && (string.IsNullOrWhiteSpace(status) || x.Status == status) && x.StartDate < end && x.EndDate >= start);
            return Ok(await query.GroupBy(x => x.Status).Select(g => new LeaveReportDto { Status = g.Key, RequestCount = g.Count(), LeaveDays = g.Sum(x => (x.EndDate.Date - x.StartDate.Date).Days + 1) }).ToListAsync());
        }

        [HttpGet("overtime")]
        [ProducesResponseType(typeof(IReadOnlyList<OvertimeReportDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Overtime([FromQuery] int month, [FromQuery] int year, [FromQuery] int? branchId, [FromQuery] int? employeeId, [FromQuery] string? status)
        {
            if (month is < 1 or > 12 || year is < 2000 or > 2100 || branchId is <= 0 || employeeId is <= 0) return BadRequest();
            var start = new DateTime(year, month, 1); var end = start.AddMonths(1);
            if (!ResolveManagerBranch(ref branchId)) return Forbid();
            var query = _context.OvertimeRecords.AsNoTracking().Where(x => (!branchId.HasValue || x.Employee!.BranchId == branchId) && (!employeeId.HasValue || x.EmployeeId == employeeId) && (string.IsNullOrWhiteSpace(status) || x.Status == status) && x.WorkDate >= start && x.WorkDate < end);
            return Ok(await query.GroupBy(x => x.Status).Select(g => new OvertimeReportDto { Status = g.Key, RequestCount = g.Count(), TotalHours = g.Sum(x => x.Hours) }).ToListAsync());
        }

        private bool ResolveManagerBranch(ref int? branchId)
        {
            if (!User.IsInRole("Manager")) return true;
            if (!int.TryParse(User.FindFirst("BranchId")?.Value, out var ownBranch)) return false;
            if (branchId.HasValue && branchId != ownBranch) return false;
            branchId = ownBranch; return true;
        }

        // Báo cáo tổng hợp công tháng theo Nhân viên
        [HttpGet("monthly-summary")]
        [ProducesResponseType(typeof(IEnumerable<MonthlyAttendanceSummaryResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMonthlySummary([FromQuery] int month, [FromQuery] int year, [FromQuery] int? branchId)
        {
            if (month is < 1 or > 12 || year is < 2000 or > 2100 || branchId is <= 0)
                return BadRequest(new { message = "Tháng, năm hoặc chi nhánh không hợp lệ." });
            var startDate = new DateTime(year, month, 1);
            var nextMonth = startDate.AddMonths(1);
            if (!ResolveManagerBranch(ref branchId))
                return Forbid();

            var query = _context.Employees.AsQueryable();
            if (branchId.HasValue) query = query.Where(e => e.BranchId == branchId.Value);

            var employees = await query.ToListAsync();
            var attendances = await _context.Attendances
                .Where(a => a.WorkDate >= startDate && a.WorkDate < nextMonth)
                .ToListAsync();

            var leaveRequests = await _context.LeaveRequests
                .Where(l => l.Status == "APPROVED" && l.StartDate < nextMonth && l.EndDate >= startDate)
                .ToListAsync();

            var result = employees.Select(emp =>
            {
                var empAttendances = attendances.Where(a => a.EmployeeId == emp.Id).ToList();
                var empLeaves = leaveRequests.Where(l => l.EmployeeId == emp.Id).ToList();

                return new MonthlyAttendanceSummaryResponse
                {
                    EmployeeId = emp.Id,
                    EmployeeCode = emp.EmployeeCode,
                    FullName = emp.FullName,
                    TotalWorkDays = empAttendances.Count(a => a.CheckOutTime != null),
                    TotalLateMinutes = empAttendances.Sum(a => a.LateMinutes),
                    TotalEarlyLeaveMinutes = empAttendances.Sum(a => a.EarlyLeaveMinutes),
                    TotalWorkedHours = Math.Round(empAttendances.Sum(a => a.WorkedMinutes) / 60.0, 2),
                    TotalApprovedLeaveDays = empLeaves.Sum(l => (l.EndDate - l.StartDate).Days + 1)
                };
            });

            return Ok(result);
        }
    }
}