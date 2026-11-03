using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class IndexModel : AdminPageModel
{
    private readonly AppDbContext _context;
    public string AdminName { get; private set; } = string.Empty;
    public int BranchId { get; private set; }
    public List<Branch> Branches { get; private set; } = [];
    public int TotalEmployees { get; private set; }
    public int CheckedInCount { get; private set; }
    public int NotCheckedInCount => Math.Max(0, TotalEmployees - CheckedInCount);
    public int LateCount { get; private set; }
    public int PendingLeaves { get; private set; }
    public decimal AttendanceRate => TotalEmployees == 0 ? 0 : CheckedInCount * 100m / TotalEmployees;

    public IndexModel(AppDbContext context) => _context = context;

    public async Task<IActionResult> OnGetAsync(int? branchId)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        AdminName = HttpContext.Session.GetString("AdminEmployeeName") ?? "Quản trị viên";
        Branches = await _context.Branches
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();

        BranchId = branchId.GetValueOrDefault();
        if (BranchId != 0 && !Branches.Any(x => x.Id == BranchId))
            BranchId = 0;

        var today = DateTime.Today;
        var employees = _context.Employees
            .AsNoTracking()
            .Where(x => x.IsActive);
        if (BranchId != 0)
            employees = employees.Where(x => x.BranchId == BranchId);

        var employeeIds = await employees.Select(x => x.Id).ToArrayAsync();
        TotalEmployees = employeeIds.Length;
        var attendanceQuery = _context.Attendances
            .AsNoTracking()
            .Where(x => x.WorkDate == today && employeeIds.Contains(x.EmployeeId));
        if (BranchId != 0)
            attendanceQuery = attendanceQuery.Where(x => x.BranchId == BranchId);

        var attendance = await attendanceQuery.ToListAsync();
        CheckedInCount = attendance.Count(x => x.CheckInTime != default);
        LateCount = attendance.Count(x => x.LateMinutes > 0);
        PendingLeaves = await _context.LeaveRequests
            .CountAsync(x => x.Status == "PENDING" && employeeIds.Contains(x.EmployeeId));
        return Page();
    }
}
