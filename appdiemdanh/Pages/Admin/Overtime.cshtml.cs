using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class OvertimeModel : AdminPageModel
{
    private readonly AppDbContext _context;
    private readonly IOvertimeService _service;
    public List<OvertimeRecord> Items { get; private set; } = [];
    public List<OvertimeRecord> ApprovedItems { get; private set; } = [];
    public string? Message { get; set; }
    public OvertimeModel(AppDbContext context, IOvertimeService service) { _context = context; _service = service; }
    public async Task<IActionResult> OnGetAsync() { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Overtime", "View")) return Forbid(); await LoadAsync(); return Page(); }
    public async Task<IActionResult> OnPostAsync(long id, bool approved) { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Overtime", "Approve")) { Message = "Bạn không có quyền duyệt OT."; await LoadAsync(); return Page(); } if (id <= 0) { Message = "Mã bản ghi OT không hợp lệ."; await LoadAsync(); return Page(); } var result = await _service.ReviewAsync(id, new OvertimeReviewRequest { IsApproved = approved }); Message = result == null ? "Không thể xử lý bản ghi OT." : "Đã cập nhật bản ghi OT."; await LoadAsync(); return Page(); }
    public async Task<IActionResult> OnPostDeleteAsync(long id) { if (!IsSystemAdmin) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Overtime", "Delete")) return Forbid(); if (id <= 0) { Message = "Mã bản ghi OT không hợp lệ."; await LoadAsync(); return Page(); } var item = await _context.OvertimeRecords.FindAsync(id); if (item != null) { Message = "Xóa OT chưa được chuyển sang soft delete vì model hiện tại chưa có cờ xóa."; await LoadAsync(); return Page(); } await LoadAsync(); return Page(); }
    private async Task LoadAsync()
    {
        Items = await _context.OvertimeRecords.Include(x => x.Employee).Where(x => x.Status.ToUpper() == "PENDING").OrderByDescending(x => x.WorkDate).ThenByDescending(x => x.Id).ToListAsync();
        ApprovedItems = await _context.OvertimeRecords.Include(x => x.Employee).Where(x => x.Status.ToUpper() == "APPROVED").OrderByDescending(x => x.WorkDate).ThenByDescending(x => x.Id).ToListAsync();
    }
}
