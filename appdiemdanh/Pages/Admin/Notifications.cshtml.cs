using AppDiemDanh.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class NotificationsModel : AdminPageModel
{
    private readonly AppDbContext _context;
    public List<NotificationLog> Items { get; private set; } = [];
    public NotificationsModel(AppDbContext context) => _context = context;
    public async Task<IActionResult> OnGetAsync() { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Notifications", "View")) return Forbid(); Items = await _context.NotificationLogs.Include(x => x.Employee).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(200).ToListAsync(); return Page(); }
}
