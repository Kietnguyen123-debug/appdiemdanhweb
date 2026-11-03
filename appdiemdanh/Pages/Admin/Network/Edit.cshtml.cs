using AppDiemDanh.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.RegularExpressions;

namespace AppDiemDanh.Pages.Admin.Network;

public class EditModel : AdminPageModel
{
    private readonly AppDbContext _context;

    [BindProperty] public BranchPublicIp IpInput { get; set; } = new();
    [BindProperty] public BranchWifi WifiInput { get; set; } = new();
    public List<Branch> Branches { get; private set; } = [];
    public string Type { get; private set; } = "ip";
    public string? ErrorMessage { get; set; }

    public EditModel(AppDbContext context) => _context = context;

    public async Task<IActionResult> OnGetAsync(string type, int id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!IsValidType(type) || id <= 0) return NotFound();
        Type = type.ToLowerInvariant();
        await LoadBranchesAsync();
        if (Type == "ip")
        {
            var item = await _context.BranchPublicIps.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (item == null) return NotFound();
            IpInput = item;
        }
        else
        {
            var item = await _context.BranchWifis.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (item == null) return NotFound();
            WifiInput = item;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string type, int id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!IsValidType(type) || id <= 0) return NotFound();
        Type = type.ToLowerInvariant();
        await LoadBranchesAsync();

        if (Type == "ip")
        {
            var item = await _context.BranchPublicIps.FindAsync(id);
            if (item == null) return NotFound();
            var parsedIp = IPAddress.TryParse(IpInput.PublicIp?.Trim(), out var ip) ? ip : null;
            if (!await _context.Branches.AnyAsync(x => x.Id == IpInput.BranchId && x.IsActive) || string.IsNullOrWhiteSpace(IpInput.PublicIp) || parsedIp == null)
            {
                ErrorMessage = "Chi nhánh hoặc địa chỉ IP không hợp lệ.";
                return Page();
            }
            item.BranchId = IpInput.BranchId;
            item.PublicIp = parsedIp.ToString();
            item.Description = IpInput.Description?.Trim() ?? string.Empty;
        }
        else
        {
            var item = await _context.BranchWifis.FindAsync(id);
            if (item == null) return NotFound();
            if (!await _context.Branches.AnyAsync(x => x.Id == WifiInput.BranchId && x.IsActive) || string.IsNullOrWhiteSpace(WifiInput.SSID) || WifiInput.SSID.Trim().Length > 32 || string.IsNullOrWhiteSpace(WifiInput.BSSID) || !Regex.IsMatch(WifiInput.BSSID.Trim(), "^[0-9A-Fa-f]{2}([-:])[0-9A-Fa-f]{2}(\\1[0-9A-Fa-f]{2}){4}$"))
            {
                ErrorMessage = "Chi nhánh, SSID hoặc BSSID không hợp lệ.";
                return Page();
            }
            item.BranchId = WifiInput.BranchId;
            item.SSID = WifiInput.SSID.Trim();
            item.BSSID = WifiInput.BSSID.Trim().ToUpperInvariant();
            item.Description = WifiInput.Description?.Trim() ?? string.Empty;
        }

        await _context.SaveChangesAsync();
        return RedirectToPage("/Admin/Network");
    }

    private static bool IsValidType(string? type) => string.Equals(type, "ip", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "wifi", StringComparison.OrdinalIgnoreCase);

    private async Task LoadBranchesAsync() => Branches = await _context.Branches.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
}
