using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin.Shifts;

public class AssignModel : AdminPageModel
{
    private readonly AppDbContext _context;
    private readonly IShiftService _service;
    [BindProperty] public AssignShiftRequest Input { get; set; } = new() { EffectiveFromDate = DateTime.Today };
    public List<Employee> Employees { get; private set; } = [];
    public List<Shift> Shifts { get; private set; } = [];
    public string? Message { get; set; }
    public bool Success { get; set; }

    public AssignModel(AppDbContext context, IShiftService service) { _context = context; _service = service; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (Input == null || Input.EmployeeId <= 0 || Input.ShiftId <= 0 || Input.EffectiveFromDate == default || (Input.EffectiveToDate.HasValue && Input.EffectiveToDate.Value.Date < Input.EffectiveFromDate.Date))
        {
            Success = false;
            Message = "Nhân viên, ca làm việc và khoảng thời gian phải hợp lệ.";
            await LoadAsync();
            return Page();
        }
        var result = await _service.AssignShiftToEmployeeAsync(Input);
        Success = result.HasValue;
        Message = Success ? "Đã phân ca thành công." : "Không thể phân ca. Kiểm tra thông tin hoặc lịch bị trùng.";
        if (!Success) await LoadAsync();
        return Success ? RedirectToPage("/Admin/Shifts") : Page();
    }

    private async Task LoadAsync()
    {
        Employees = await _context.Employees.Where(x => x.IsActive).OrderBy(x => x.EmployeeCode).ToListAsync();
        Shifts = await _context.Shifts.Where(x => x.IsActive).OrderBy(x => x.StartTime).ToListAsync();
    }
}
