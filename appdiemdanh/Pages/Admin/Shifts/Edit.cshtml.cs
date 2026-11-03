using AppDiemDanh.DTOs;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Mvc;

namespace AppDiemDanh.Pages.Admin.Shifts;

public class EditModel : AdminPageModel
{
    private readonly IShiftService _service;
    [BindProperty] public CreateShiftRequest Input { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public EditModel(IShiftService service) => _service = service;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (id <= 0) return BadRequest("Mã ca không hợp lệ.");
        var item = await _service.GetByIdAsync(id);
        if (item == null) return NotFound();
        Input = new CreateShiftRequest { ShiftCode = item.ShiftCode, ShiftName = item.ShiftName, StartTime = item.StartTime, EndTime = item.EndTime, IsActive = item.IsActive };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (id <= 0 || Input == null || string.IsNullOrWhiteSpace(Input.ShiftCode) || string.IsNullOrWhiteSpace(Input.ShiftName) || Input.StartTime < TimeSpan.Zero || Input.StartTime >= TimeSpan.FromDays(1) || Input.EndTime < TimeSpan.Zero || Input.EndTime >= TimeSpan.FromDays(1) || Input.StartTime == Input.EndTime)
        {
            ErrorMessage = "Thông tin ca không hợp lệ.";
            return Page();
        }
        var item = await _service.UpdateAsync(id, Input);
        if (item == null) { ErrorMessage = "Không thể cập nhật ca. Mã ca có thể đã tồn tại hoặc dữ liệu chưa hợp lệ."; return Page(); }
        return RedirectToPage("/Admin/Shifts");
    }
}
