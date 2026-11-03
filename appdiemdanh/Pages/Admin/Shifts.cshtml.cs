using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class ShiftsModel : AdminPageModel
{
    private readonly AppDbContext _context;
    private readonly IShiftService _service;
    public List<Shift> Shifts { get; private set; } = [];
    public List<Employee> Employees { get; private set; } = [];
    public List<EmployeeShift> Assignments { get; private set; } = [];
    [BindProperty] public CreateShiftRequest Shift { get; set; } = new();
    [BindProperty] public AssignShiftRequest Assignment { get; set; } = new() { EffectiveFromDate = DateTime.Today };
    public string? Message { get; set; }
    public ShiftsModel(AppDbContext context, IShiftService service) { _context = context; _service = service; }
    public async Task<IActionResult> OnGetAsync() { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); await LoadAsync(); return Page(); }
    public async Task<IActionResult> OnPostAsync() { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (Shift == null || string.IsNullOrWhiteSpace(Shift.ShiftCode) || string.IsNullOrWhiteSpace(Shift.ShiftName) || Shift.StartTime == Shift.EndTime) { Message = "Vui lòng nhập đầy đủ thông tin ca hợp lệ."; await LoadAsync(); return Page(); } var result = await _service.CreateAsync(Shift); Message = result == null ? "Không thể tạo ca. Mã ca có thể đã tồn tại." : "Đã tạo ca."; await LoadAsync(); return Page(); }
    public async Task<IActionResult> OnPostAssignAsync() { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (Assignment == null || Assignment.EmployeeId <= 0 || Assignment.ShiftId <= 0 || Assignment.EffectiveFromDate == default || (Assignment.EffectiveToDate.HasValue && Assignment.EffectiveToDate < Assignment.EffectiveFromDate)) { Message = "Thông tin phân ca không hợp lệ."; await LoadAsync(); return Page(); } var result = await _service.AssignShiftToEmployeeAsync(Assignment); Message = result.HasValue ? "Đã phân ca thành công và ghi đè khoảng bị trùng." : "Không thể phân ca. Vui lòng kiểm tra nhân viên và ca làm việc."; await LoadAsync(); return Page(); }
    public async Task<IActionResult> OnPostDeleteAsync(int id) { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (id <= 0) { Message = "Mã ca không hợp lệ."; await LoadAsync(); return Page(); } var result = await _service.DeleteAsync(id); if (!result) Message = "Không tìm thấy ca cần xóa."; await LoadAsync(); return Page(); }
    public async Task<IActionResult> OnPostDeleteAssignmentAsync(long id) { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (id <= 0) { Message = "Mã phân ca không hợp lệ."; await LoadAsync(); return Page(); } var assignment = await _context.EmployeeShifts.FindAsync(id); if (assignment == null) Message = "Không tìm thấy phân ca cần xóa."; else { _context.EmployeeShifts.Remove(assignment); await _context.SaveChangesAsync(); Message = "Đã xóa phân ca."; } await LoadAsync(); return Page(); }
    private async Task LoadAsync() { Shifts = await _context.Shifts.OrderByDescending(x => x.Id).ToListAsync(); Employees = await _context.Employees.Where(x => x.IsActive).OrderBy(x => x.EmployeeCode).ToListAsync(); Assignments = await _context.EmployeeShifts.Include(x => x.Employee).Include(x => x.Shift).OrderByDescending(x => x.EffectiveFromDate).ThenByDescending(x => x.Id).ThenBy(x => x.Employee!.EmployeeCode).ToListAsync(); }
}
