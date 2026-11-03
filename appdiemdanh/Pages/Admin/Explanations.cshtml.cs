using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Mvc;

namespace AppDiemDanh.Pages.Admin;

public class ExplanationsModel : AdminPageModel
{
    private readonly IAttendanceExplanationService _service;
    public IReadOnlyList<AttendanceExplanationResponse> Items { get; private set; } = [];
    public IReadOnlyList<AttendanceExplanationResponse> ApprovedItems { get; private set; } = [];
    public string? Message { get; set; }
    public ExplanationsModel(IAttendanceExplanationService service) => _service = service;
    public async Task<IActionResult> OnGetAsync() { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Explanations", "View")) return Forbid(); await LoadAsync(); return Page(); }
    public async Task<IActionResult> OnPostAsync(long id, string status) { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Explanations", "Approve")) { Message = "Bạn không có quyền duyệt giải trình."; await LoadAsync(); return Page(); } if (id <= 0 || status is not ("APPROVED" or "REJECTED")) { Message = "Thông tin xử lý giải trình không hợp lệ."; await LoadAsync(); return Page(); } var result = await _service.ReviewExplanationAsync(id, CurrentEmployeeId, status, status == "REJECTED" ? "Từ chối trên portal Admin" : null); Message = result == null ? "Không thể xử lý giải trình." : "Đã cập nhật giải trình."; await LoadAsync(); return Page(); }
    private async Task LoadAsync() { Items = await _service.GetPendingExplanationsAsync(); ApprovedItems = await _service.GetApprovedExplanationsAsync(); }
}
