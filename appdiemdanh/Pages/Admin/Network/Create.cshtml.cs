using AppDiemDanh.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.RegularExpressions;

namespace AppDiemDanh.Pages.Admin.Network;

public class CreateModel : AdminPageModel
{
    private readonly AppDbContext _context;

    [BindProperty] public BranchPublicIp IpInput { get; set; } = new();
    [BindProperty] public BranchWifi WifiInput { get; set; } = new();
    public List<Branch> Branches { get; private set; } = [];
    public string Type { get; private set; } = "ip";
    public string? ErrorMessage { get; set; }

    public CreateModel(AppDbContext context) => _context = context;

    public async Task<IActionResult> OnGetAsync(string type = "ip")
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!IsValidType(type)) return NotFound();
        Type = type.ToLowerInvariant();
        await LoadBranchesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string type)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!IsValidType(type)) return NotFound();
        Type = type.ToLowerInvariant();
        await LoadBranchesAsync();

        if (Type == "ip")
        {
            var parsedIp = IPAddress.TryParse(IpInput.PublicIp?.Trim(), out var ip) ? ip : null;
            if (!await _context.Branches.AnyAsync(x => x.Id == IpInput.BranchId && x.IsActive) || string.IsNullOrWhiteSpace(IpInput.PublicIp) || parsedIp == null)
            {
                ErrorMessage = "Chi nhánh hoặc địa chỉ IP không hợp lệ.";
                return Page();
            }

            _context.BranchPublicIps.Add(new BranchPublicIp
            {
                BranchId = IpInput.BranchId,
                PublicIp = parsedIp.ToString(),
                Description = IpInput.Description?.Trim() ?? string.Empty,
                IsActive = true
            });
        }
        else
        {
            if (!await _context.Branches.AnyAsync(x => x.Id == WifiInput.BranchId && x.IsActive) || string.IsNullOrWhiteSpace(WifiInput.SSID) || WifiInput.SSID.Trim().Length > 32 || string.IsNullOrWhiteSpace(WifiInput.BSSID) || !Regex.IsMatch(WifiInput.BSSID.Trim(), "^[0-9A-Fa-f]{2}([-:])[0-9A-Fa-f]{2}(\\1[0-9A-Fa-f]{2}){4}$"))
            {
                ErrorMessage = "Chi nhánh, SSID hoặc BSSID không hợp lệ.";
                return Page();
            }

            _context.BranchWifis.Add(new BranchWifi
            {
                BranchId = WifiInput.BranchId,
                SSID = WifiInput.SSID.Trim(),
                BSSID = WifiInput.BSSID.Trim().ToUpperInvariant(),
                Description = WifiInput.Description?.Trim() ?? string.Empty,
                IsActive = true
            });
        }

        await _context.SaveChangesAsync();
        return RedirectToPage("/Admin/Network");
    }

    private static bool IsValidType(string? type) => string.Equals(type, "ip", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "wifi", StringComparison.OrdinalIgnoreCase);

    private async Task LoadBranchesAsync() => Branches = await _context.Branches.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
}
