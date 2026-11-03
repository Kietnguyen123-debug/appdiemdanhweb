using System;
using System.Linq;
using System.Threading.Tasks;
using AppDiemDanh.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using AppDiemDanh.DTOs;

namespace AppDiemDanh.Controllers
{
    [Authorize(Roles = "Admin,HR,Manager")]
    [ApiController]
    [Route("api/[controller]")]
    public class AdminDashboardController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AdminDashboardController(AppDbContext context) => _context = context;

        [HttpGet("overview")]
        [ProducesResponseType(typeof(DashboardOverviewResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Overview([FromQuery] int? branchId, [FromQuery] DateTime? date)
        {
            if (!TryResolveBranch(branchId, out var resolvedBranchId, out var error)) return error!;
            var targetDate = (date ?? DateTime.Today).Date;
            var employees = _context.Employees.AsNoTracking().Where(x => x.IsActive && (!resolvedBranchId.HasValue || x.BranchId == resolvedBranchId));
            var employeeIds = await employees.Select(x => x.Id).ToListAsync();
            var attendance = await _context.Attendances.AsNoTracking().Where(x => x.WorkDate == targetDate && employeeIds.Contains(x.EmployeeId)).ToListAsync();
            var onLeave = await _context.LeaveRequests.AsNoTracking().CountAsync(x => x.Status == "APPROVED" && x.StartDate <= targetDate && x.EndDate >= targetDate && employeeIds.Contains(x.EmployeeId));
            return Ok(new DashboardOverviewResponse
            {
                TotalEmployees = employeeIds.Count,
                ActiveEmployees = employeeIds.Count,
                InactiveEmployees = await _context.Employees.CountAsync(x => !x.IsActive && (!resolvedBranchId.HasValue || x.BranchId == resolvedBranchId)),
                ActivatedEmployees = await employees.CountAsync(x => x.IsActivated),
                NotActivatedEmployees = await employees.CountAsync(x => !x.IsActivated),
                PresentToday = attendance.Count(x => x.CheckInTime != default),
                AbsentToday = Math.Max(0, employeeIds.Count - attendance.Count(x => x.CheckInTime != default)),
                LateToday = attendance.Count(x => x.LateMinutes > 0),
                EarlyLeaveToday = attendance.Count(x => x.EarlyLeaveMinutes > 0),
                OnLeaveToday = onLeave,
                CheckedInButNotCheckedOut = attendance.Count(x => x.CheckInTime != default && x.CheckOutTime == null),
                PendingLeaveRequests = await _context.LeaveRequests.CountAsync(x => x.Status == "PENDING" && employeeIds.Contains(x.EmployeeId)),
                PendingAttendanceExplanations = await _context.AttendanceExplanations.CountAsync(x => x.Status == "PENDING" && employeeIds.Contains(x.EmployeeId)),
                PendingOvertimeRequests = await _context.OvertimeRecords.CountAsync(x => x.Status == "Pending" && employeeIds.Contains(x.EmployeeId)),
                PendingSalaryAdvanceRequests = await _context.SalaryAdvances.CountAsync(x => x.Status == "Pending" && employeeIds.Contains(x.EmployeeId))
            });
        }

        [HttpGet("attendance-summary")]
        [ProducesResponseType(typeof(DashboardAttendanceSummaryResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> AttendanceSummary([FromQuery] int? branchId, [FromQuery] DateTime? date)
        {
            if (!TryResolveBranch(branchId, out var resolvedBranchId, out var error)) return error!;
            var targetDate = (date ?? DateTime.Today).Date;
            var employees = _context.Employees.AsNoTracking().Where(x => x.IsActive && (!resolvedBranchId.HasValue || x.BranchId == resolvedBranchId));
            var employeeIds = await employees.Select(x => x.Id).ToListAsync();
            var rows = await _context.Attendances.AsNoTracking().Where(x => x.WorkDate == targetDate && employeeIds.Contains(x.EmployeeId)).ToListAsync();
            var checkedIn = rows.Count(x => x.CheckInTime != default);
            return Ok(new DashboardAttendanceSummaryResponse { TotalEmployees = employeeIds.Count, CheckedIn = checkedIn, CheckedOut = rows.Count(x => x.CheckOutTime.HasValue), Absent = employeeIds.Count - checkedIn, Late = rows.Count(x => x.LateMinutes > 0), EarlyLeave = rows.Count(x => x.EarlyLeaveMinutes > 0), MissingCheckIn = rows.Count(x => x.CheckInTime == default), MissingCheckOut = rows.Count(x => x.CheckInTime != default && !x.CheckOutTime.HasValue), TotalLateMinutes = rows.Sum(x => x.LateMinutes), TotalEarlyLeaveMinutes = rows.Sum(x => x.EarlyLeaveMinutes) });
        }

        [HttpGet("pending-counts")]
        [ProducesResponseType(typeof(DashboardPendingCountsResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> PendingCounts([FromQuery] int? branchId)
        {
            if (!TryResolveBranch(branchId, out var resolvedBranchId, out var error)) return error!;
            var employeeIds = _context.Employees.Where(x => !resolvedBranchId.HasValue || x.BranchId == resolvedBranchId).Select(x => x.Id);
            return Ok(new DashboardPendingCountsResponse { PendingLeave = await _context.LeaveRequests.CountAsync(x => x.Status == "PENDING" && employeeIds.Contains(x.EmployeeId)), PendingAttendanceExplanation = await _context.AttendanceExplanations.CountAsync(x => x.Status == "PENDING" && employeeIds.Contains(x.EmployeeId)), PendingOvertime = await _context.OvertimeRecords.CountAsync(x => x.Status == "Pending" && employeeIds.Contains(x.EmployeeId)), PendingSalaryAdvance = await _context.SalaryAdvances.CountAsync(x => x.Status == "Pending" && employeeIds.Contains(x.EmployeeId)) });
        }

        private bool TryResolveBranch(int? requested, out int? branchId, out IActionResult? error)
        {
            branchId = requested;
            error = null;
            if (requested is <= 0) { error = BadRequest(new { message = "BranchId không hợp lệ." }); return false; }
            if (User.IsInRole("Manager"))
            {
                if (!int.TryParse(User.FindFirstValue("BranchId"), out var ownBranch) || ownBranch <= 0) { error = Forbid(); return false; }
                if (requested.HasValue && requested.Value != ownBranch) { error = Forbid(); return false; }
                branchId = ownBranch;
            }
            return true;
        }

        /// <summary>
        /// Thống kê nhanh trang chủ Admin trên App Mobile
        /// </summary>
        [HttpGet("today-summary")]
        [ProducesResponseType(typeof(TodaySummaryResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetTodaySummary([FromQuery] int? branchId)
        {
            DateTime today = DateTime.Today;

            var empQuery = _context.Employees.Where(e => e.IsActive);
            if (branchId.HasValue) empQuery = empQuery.Where(e => e.BranchId == branchId.Value);

            int totalEmployees = await empQuery.CountAsync();

            var todayAttendances = await _context.Attendances
                .Where(a => a.WorkDate == today && (!branchId.HasValue || a.BranchId == branchId.Value))
                .ToListAsync();

            int checkedInCount = todayAttendances.Count(a => a.CheckInTime != DateTime.MinValue);
            int lateCount = todayAttendances.Count(a => a.LateMinutes > 0);

            int pendingLeaves = await _context.LeaveRequests
                .CountAsync(l => l.Status == "PENDING" && l.StartDate <= today && l.EndDate >= today);

            return Ok(new TodaySummaryResponse
            {
                TotalEmployees = totalEmployees,
                CheckedInCount = checkedInCount,
                NotCheckedInCount = totalEmployees - checkedInCount,
                LateCount = lateCount,
                PendingLeaveRequests = pendingLeaves
            });
        }
    }
}