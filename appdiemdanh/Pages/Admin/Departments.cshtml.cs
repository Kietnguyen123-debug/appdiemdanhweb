using AppDiemDanh.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class DepartmentsModel : AdminPageModel
{
    private readonly AppDbContext _context;

    public List<Department> Departments { get; private set; } = [];
    [BindProperty] public string Name { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }

    public DepartmentsModel(AppDbContext context) => _context = context;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        Name = Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Tên phòng ban không được để trống.";
            await LoadAsync();
            return Page();
        }
        if (await _context.Departments.AnyAsync(x => x.Name == Name))
        {
            ErrorMessage = "Tên phòng ban đã tồn tại.";
            await LoadAsync();
            return Page();
        }
        _context.Departments.Add(new Department { Name = Name, IsActive = true });
        await _context.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        var department = await _context.Departments.FindAsync(id);
        if (department == null) return NotFound();
        department.IsActive = !department.IsActive;
        await _context.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        var department = await _context.Departments.FindAsync(id);
        if (department == null) return NotFound();
        _context.Departments.Remove(department);
        await _context.SaveChangesAsync();
        return RedirectToPage();
    }

    private async Task LoadAsync() => Departments = await _context.Departments
        .Include(x => x.Employees)
        .AsNoTracking()
        .OrderByDescending(x => x.Id)
        .ToListAsync();
}
