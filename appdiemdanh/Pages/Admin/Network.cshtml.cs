using AppDiemDanh.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class NetworkModel : AdminPageModel
{
    private readonly AppDbContext _context;
    public List<Branch> Branches { get; private set; } = [];
    public List<BranchPublicIp> Ips { get; private set; } = [];
    public List<BranchWifi> Wifis { get; private set; } = [];
    public string? Message { get; set; }
    public bool Success { get; set; }

    public NetworkModel(AppDbContext context) => _context = context;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!await HasPermissionAsync("Network", "View")) return Forbid();
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostToggleIpAsync(int id) { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Network", "Edit")) return Forbid(); if (id <= 0) return BadRequest("Mã IP không hợp lệ."); var item = await _context.BranchPublicIps.FindAsync(id); if (item != null) { item.IsActive = !item.IsActive; await _context.SaveChangesAsync(); } return RedirectToPage(); }
    public async Task<IActionResult> OnPostToggleWifiAsync(int id) { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Network", "Edit")) return Forbid(); if (id <= 0) return BadRequest("Mã Wi-Fi không hợp lệ."); var item = await _context.BranchWifis.FindAsync(id); if (item != null) { item.IsActive = !item.IsActive; await _context.SaveChangesAsync(); } return RedirectToPage(); }
    public async Task<IActionResult> OnPostDeleteIpAsync(int id) { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Network", "Delete")) return Forbid(); if (id <= 0) return BadRequest("Mã IP không hợp lệ."); var item = await _context.BranchPublicIps.FindAsync(id); if (item == null) return NotFound(); _context.BranchPublicIps.Remove(item); await _context.SaveChangesAsync(); return RedirectToPage(); }
    public async Task<IActionResult> OnPostDeleteWifiAsync(int id) { if (!IsAdminSession) return RedirectToPage("/Admin/Login"); if (!await HasPermissionAsync("Network", "Delete")) return Forbid(); if (id <= 0) return BadRequest("Mã Wi-Fi không hợp lệ."); var item = await _context.BranchWifis.FindAsync(id); if (item == null) return NotFound(); _context.BranchWifis.Remove(item); await _context.SaveChangesAsync(); return RedirectToPage(); }

    private async Task<IActionResult> FailAsync(string message) { Message = message; Success = false; await LoadAsync(); return Page(); }
    private async Task LoadAsync()
    {
        Branches = await _context.Branches.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        Ips = await _context.BranchPublicIps.Include(x => x.Branch).OrderByDescending(x => x.Id).ToListAsync();
        Wifis = await _context.BranchWifis.Include(x => x.Branch).OrderByDescending(x => x.Id).ToListAsync();
    }
}
