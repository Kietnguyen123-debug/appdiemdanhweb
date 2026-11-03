using AppDiemDanh.Models;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class LeavesModel : AdminPageModel
{
    private readonly AppDbContext _context;
    private readonly ILeaveService _service;
    public List<LeaveRequest> Items { get; private set; } = [];
    public List<LeaveRequest> ApprovedItems { get; private set; } = [];
    public string? Message { get; set; }
    public LeavesModel(AppDbContext context, ILeaveService service) { _context = context; _service = service; }
    public async Task<IActionResult> OnGetAsync() { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Leave", "View")) return Forbid(); await LoadAsync(); return Page(); }
    public async Task<IActionResult> OnPostAsync(long id, bool approved) { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Leave", "Approve")) { Message = "Bạn không có quyền duyệt đơn nghỉ phép."; await LoadAsync(); return Page(); } if (id <= 0) { Message = "Mã đơn nghỉ không hợp lệ."; await LoadAsync(); return Page(); } var result = await _service.ApproveLeaveAsync(id, CurrentEmployeeId, approved, approved ? null : "Từ chối trên portal Admin"); Message = result.Message; await LoadAsync(); return Page(); }
    private async Task LoadAsync()
    {
        Items = await _context.LeaveRequests.Include(x => x.Employee).Where(x => x.Status.ToUpper() == "PENDING").OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToListAsync();
        ApprovedItems = await _context.LeaveRequests.Include(x => x.Employee).Where(x => x.Status.ToUpper() == "APPROVED").OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToListAsync();
    }
}
