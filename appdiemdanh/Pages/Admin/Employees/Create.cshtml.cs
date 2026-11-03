using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;

namespace AppDiemDanh.Pages.Admin.Employees;

public class CreateModel : AdminPageModel
{
    private readonly AppDbContext _context;
    private readonly PasswordHasher<string> _hasher = new();

    [BindProperty] public CreateEmployeeAccountRequest Input { get; set; } = new();
    public List<Branch> Branches { get; private set; } = [];
    public List<Department> Departments { get; private set; } = [];
    public string? ErrorMessage { get; set; }

    public CreateModel(AppDbContext context) => _context = context;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        await LoadBranchesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        Input ??= new();
        Input.EmployeeCode = Input.EmployeeCode?.Trim() ?? string.Empty;
        Input.FullName = Input.FullName?.Trim() ?? string.Empty;
        Input.Role = Input.Role?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Input.EmployeeCode) || string.IsNullOrWhiteSpace(Input.FullName) ||
            string.IsNullOrWhiteSpace(Input.TemporaryPassword) || Input.TemporaryPassword.Length < 6 ||
            Input.BranchId <= 0 || Input.Role is not ("Admin" or "HR" or "Manager" or "Employee"))
        {
            ErrorMessage = "Mã nhân viên, họ tên, mật khẩu, vai trò và chi nhánh phải hợp lệ.";
            await LoadBranchesAsync();
            return Page();
        }
        if (!await _context.Branches.AnyAsync(x => x.Id == Input.BranchId && x.IsActive))
        {
            ErrorMessage = "Chi nhánh không tồn tại hoặc đã bị khóa.";
            await LoadBranchesAsync();
            return Page();
        }
        if (Input.DepartmentId.HasValue && !await _context.Departments.AnyAsync(x => x.Id == Input.DepartmentId.Value && x.IsActive))
        {
            ErrorMessage = "Phòng ban không tồn tại hoặc đã bị khóa.";
            await LoadBranchesAsync();
            return Page();
        }
        Input.Email = NormalizeEmail(Input.Email);
        if (!string.IsNullOrWhiteSpace(Input.Email) && !MailAddress.TryCreate(Input.Email, out _))
        {
            ErrorMessage = "Email không hợp lệ.";
            await LoadBranchesAsync();
            return Page();
        }
        if (!string.IsNullOrWhiteSpace(Input.Email) && await _context.Employees.AnyAsync(x => x.Email == Input.Email))
        {
            ErrorMessage = "Email đã được sử dụng.";
            await LoadBranchesAsync();
            return Page();
        }
        if (await _context.Employees.AnyAsync(x => x.EmployeeCode == Input.EmployeeCode.Trim()))
        {
            ErrorMessage = "Mã nhân viên đã tồn tại.";
            await LoadBranchesAsync();
            return Page();
        }

        _context.Employees.Add(new Employee
        {
            EmployeeCode = Input.EmployeeCode.Trim(),
            FullName = Input.FullName.Trim(),
            BranchId = Input.BranchId,
            DepartmentId = Input.DepartmentId,
            Role = Input.Role,
            Email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email,
            IsEmailVerified = false,
            PasswordHash = _hasher.HashPassword(Input.EmployeeCode, Input.TemporaryPassword),
            IsActive = true,
            IsActivated = !RequiresFirstTimeActivation(Input.Role)
        });
        await _context.SaveChangesAsync();
        return RedirectToPage("/Admin/Employees");
    }

    private async Task LoadBranchesAsync()
    {
        Branches = await _context.Branches.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        Departments = await _context.Departments.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
    }
    private static string? NormalizeEmail(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    private static bool RequiresFirstTimeActivation(string? role) => string.Equals(role?.Trim(), "Employee", StringComparison.OrdinalIgnoreCase);
}
