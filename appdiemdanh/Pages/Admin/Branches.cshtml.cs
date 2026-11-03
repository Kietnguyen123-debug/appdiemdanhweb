using AppDiemDanh.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class BranchesModel : AdminPageModel
{
    private readonly AppDbContext _context;
    public List<Branch> Branches { get; private set; } = [];
    public Dictionary<int, int> EmployeeCounts { get; private set; } = [];
    public string? Message { get; set; }

    [BindProperty]
    public string NewBranchName { get; set; } = string.Empty;

    public BranchesModel(AppDbContext context) => _context = context;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!await HasPermissionAsync("Branches", "View")) return Forbid();
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!await HasPermissionAsync("Branches", "Create")) return Forbid();

        NewBranchName = NewBranchName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(NewBranchName) || NewBranchName.Length > 200)
        {
            Message = "Tên chi nhánh không hợp lệ.";
            await LoadAsync();
            return Page();
        }

        if (await _context.Branches.AnyAsync(x => x.Name == NewBranchName))
        {
            Message = "Chi nhánh đã tồn tại.";
            await LoadAsync();
            return Page();
        }

        _context.Branches.Add(new Branch { Name = NewBranchName, IsActive = true });
        await _context.SaveChangesAsync();
        Message = "Đã thêm chi nhánh thành công.";
        NewBranchName = string.Empty;
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!await HasPermissionAsync("Branches", "Edit")) return Forbid();
        if (id <= 0) return BadRequest("Mã chi nhánh không hợp lệ.");

        var branch = await _context.Branches.FindAsync(id);
        if (branch != null)
        {
            branch.IsActive = !branch.IsActive;
            await _context.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!await HasPermissionAsync("Branches", "Delete")) return Forbid();
        if (id <= 0) return BadRequest("Mã chi nhánh không hợp lệ.");

        var branch = await _context.Branches.FindAsync(id);
        if (branch == null) return NotFound();

        if (await _context.Employees.AnyAsync(x => x.BranchId == id))
        {
            Message = "Không thể xóa chi nhánh đang có nhân viên.";
            await LoadAsync();
            return Page();
        }

        _context.Branches.Remove(branch);
        await _context.SaveChangesAsync();
        Message = "Đã xóa chi nhánh thành công.";
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        Branches = await _context.Branches.OrderByDescending(x => x.Id).ToListAsync();
        EmployeeCounts = await _context.Employees
            .GroupBy(x => x.BranchId)
            .Select(x => new { BranchId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.BranchId, x => x.Count);
    }
}
