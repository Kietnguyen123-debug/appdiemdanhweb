using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class AdvancesModel : AdminPageModel
{
    private readonly AppDbContext _context;
    private readonly ISalaryAdvanceService _service;
    public List<SalaryAdvance> Items { get; private set; } = [];
    public List<SalaryAdvance> ApprovedItems { get; private set; } = [];
    public string? Message { get; set; }
    public AdvancesModel(AppDbContext context, ISalaryAdvanceService service) { _context = context; _service = service; }
    public async Task<IActionResult> OnGetAsync() { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Advances", "View")) return Forbid(); await LoadAsync(); return Page(); }
    public async Task<IActionResult> OnPostAsync(long id, bool approved) { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Advances", "Approve")) { Message = "Bạn không có quyền duyệt tạm ứng."; await LoadAsync(); return Page(); } if (id <= 0) { Message = "Mã đơn tạm ứng không hợp lệ."; await LoadAsync(); return Page(); } var result = await _service.ReviewAsync(id, CurrentEmployeeId, new SalaryAdvanceReviewRequest { IsApproved = approved, ReviewNote = approved ? null : "Từ chối trên portal Admin" }); Message = result == null ? "Không thể xử lý đơn tạm ứng." : "Đã cập nhật đơn tạm ứng."; await LoadAsync(); return Page(); }
    public async Task<IActionResult> OnPostDeleteAsync(long id) { if (!IsSystemAdmin) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Advances", "Delete")) return Forbid(); if (id <= 0) { Message = "Mã đơn tạm ứng không hợp lệ."; await LoadAsync(); return Page(); } var item = await _context.SalaryAdvances.FindAsync(id); if (item != null) { Message = "Xóa tạm ứng chưa được chuyển sang soft delete vì model hiện tại chưa có cờ xóa."; await LoadAsync(); return Page(); } await LoadAsync(); return Page(); }
    private async Task LoadAsync()
    {
        Items = await _context.SalaryAdvances.Include(x => x.Employee).Where(x => x.Status.ToUpper() == "PENDING").OrderByDescending(x => x.RequestDate).ThenByDescending(x => x.Id).ToListAsync();
        ApprovedItems = await _context.SalaryAdvances.Include(x => x.Employee).Where(x => x.Status.ToUpper() == "APPROVED").OrderByDescending(x => x.RequestDate).ThenByDescending(x => x.Id).ToListAsync();
    }
}
