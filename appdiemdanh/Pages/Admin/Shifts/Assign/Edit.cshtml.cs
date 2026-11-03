using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin.Shifts.Assign;

public class EditModel : AdminPageModel
{
    private readonly AppDbContext _context;

    [BindProperty]
    public AssignShiftRequest Input { get; set; } = new();

    public List<Employee> Employees { get; private set; } = [];
    public List<Shift> Shifts { get; private set; } = [];
    public string? ErrorMessage { get; set; }

    public EditModel(AppDbContext context) => _context = context;

    public async Task<IActionResult> OnGetAsync(long id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");

        var assignment = await _context.EmployeeShifts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (assignment == null) return NotFound();

        Input = new AssignShiftRequest
        {
            EmployeeId = assignment.EmployeeId,
            ShiftId = assignment.ShiftId,
            EffectiveFromDate = assignment.EffectiveFromDate,
            EffectiveToDate = assignment.EffectiveToDate
        };

        await LoadOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");

        if (id <= 0 || Input == null || Input.EmployeeId <= 0 || Input.ShiftId <= 0 || Input.EffectiveFromDate == default ||
            Input.EffectiveToDate.HasValue && Input.EffectiveToDate.Value.Date < Input.EffectiveFromDate.Date)
        {
            ErrorMessage = "Khoảng thời gian hiệu lực không hợp lệ.";
            await LoadOptionsAsync();
            return Page();
        }

        var assignment = await _context.EmployeeShifts.FirstOrDefaultAsync(x => x.Id == id);
        if (assignment == null) return NotFound();

        var employeeExists = await _context.Employees.AnyAsync(x => x.Id == Input.EmployeeId && x.IsActive);
        var shiftExists = await _context.Shifts.AnyAsync(x => x.Id == Input.ShiftId && x.IsActive);
        if (!employeeExists || !shiftExists)
        {
            ErrorMessage = "Nhân viên hoặc ca làm việc không tồn tại hoặc đã bị vô hiệu hóa.";
            await LoadOptionsAsync();
            return Page();
        }

        var from = Input.EffectiveFromDate.Date;
        var to = Input.EffectiveToDate?.Date;
        var hasOverlap = await _context.EmployeeShifts.AnyAsync(x =>
            x.Id != id &&
            x.EmployeeId == Input.EmployeeId &&
            x.EffectiveFromDate <= (to ?? DateTime.MaxValue) &&
            (!x.EffectiveToDate.HasValue || x.EffectiveToDate.Value >= from));

        if (hasOverlap)
        {
            ErrorMessage = "Nhân viên đã có một phân ca bị trùng khoảng thời gian.";
            await LoadOptionsAsync();
            return Page();
        }

        assignment.EmployeeId = Input.EmployeeId;
        assignment.ShiftId = Input.ShiftId;
        assignment.EffectiveFromDate = from;
        assignment.EffectiveToDate = to;
        await _context.SaveChangesAsync();

        return RedirectToPage("/Admin/Shifts");
    }

    private async Task LoadOptionsAsync()
    {
        Employees = await _context.Employees
            .Where(x => x.IsActive)
            .OrderBy(x => x.EmployeeCode)
            .ToListAsync();
        Shifts = await _context.Shifts
            .Where(x => x.IsActive)
            .OrderBy(x => x.StartTime)
            .ToListAsync();
    }
}
