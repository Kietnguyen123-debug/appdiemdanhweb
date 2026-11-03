using AppDiemDanh.DTOs;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Mvc;

namespace AppDiemDanh.Pages.Admin.Shifts;

public class CreateModel : AdminPageModel
{
    private readonly IShiftService _service;
    [BindProperty] public CreateShiftRequest Input { get; set; } = new() { IsActive = true };
    public string? ErrorMessage { get; set; }

    public CreateModel(IShiftService service) => _service = service;

    public IActionResult OnGet() => IsAdminSession ? Page() : RedirectToPage("/Admin/Login");

    public async Task<IActionResult> OnPostAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (Input == null || string.IsNullOrWhiteSpace(Input.ShiftCode) || string.IsNullOrWhiteSpace(Input.ShiftName) || Input.StartTime < TimeSpan.Zero || Input.StartTime >= TimeSpan.FromDays(1) || Input.EndTime < TimeSpan.Zero || Input.EndTime >= TimeSpan.FromDays(1) || Input.StartTime == Input.EndTime)
        {
            ErrorMessage = "Mã ca, tên ca và thời gian ca không được để trống hoặc trùng nhau.";
            return Page();
        }
        var result = await _service.CreateAsync(Input);
        if (result == null)
        {
            ErrorMessage = "Không thể tạo ca. Mã ca có thể đã tồn tại hoặc thông tin chưa hợp lệ.";
            return Page();
        }
        return RedirectToPage("/Admin/Shifts");
    }
}
