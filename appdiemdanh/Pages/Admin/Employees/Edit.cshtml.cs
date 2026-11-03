using AppDiemDanh.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;

namespace AppDiemDanh.Pages.Admin.Employees;

public class EditModel : AdminPageModel
{
    private readonly AppDbContext _context;

    [BindProperty]
    public EmployeeInput Input { get; set; } = new();

    [BindProperty]
    public string? NewTemporaryPassword { get; set; }

    public List<Branch> Branches { get; private set; } = [];

    public List<Department> Departments { get; private set; } = [];

    public string? ErrorMessage { get; set; }

    public string? SuccessMessage { get; set; }

    public EditModel(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!IsAdminSession)
            return RedirectToPage("/Admin/Login");

        var employee = await _context.Employees.FindAsync(id);

        if (employee == null)
            return NotFound();

        Input = EmployeeInput.From(employee);

        await LoadBranchesAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!IsAdminSession)
            return RedirectToPage("/Admin/Login");

        if (id <= 0)
            return BadRequest("Mã nhân viên không hợp lệ.");

        // Tìm nhân viên trước khi thực hiện các xử lý liên quan
        var employee = await _context.Employees.FindAsync(id);

        if (employee == null)
            return NotFound();

        Input ??= new();

        Input.EmployeeCode = Input.EmployeeCode?.Trim() ?? string.Empty;
        Input.FullName = Input.FullName?.Trim() ?? string.Empty;
        Input.Role = Input.Role?.Trim() ?? string.Empty;

        NewTemporaryPassword = string.IsNullOrWhiteSpace(NewTemporaryPassword)
            ? null
            : NewTemporaryPassword.Trim();

        if (string.IsNullOrWhiteSpace(Input.EmployeeCode) ||
            string.IsNullOrWhiteSpace(Input.FullName) ||
            Input.BranchId <= 0 ||
            Input.Role is not ("Admin" or "HR" or "Manager" or "Employee"))
        {
            ErrorMessage = "Mã nhân viên, họ tên, vai trò và chi nhánh phải hợp lệ.";
            await LoadBranchesAsync();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(NewTemporaryPassword))
        {
            if (NewTemporaryPassword.Length < 6 || NewTemporaryPassword.Length > 128)
            {
                ErrorMessage = "Mật khẩu tạm phải có từ 6 đến 128 ký tự.";
                await LoadBranchesAsync();
                return Page();
            }
        }

        if (Input.DepartmentId.HasValue &&
            !await _context.Departments.AnyAsync(x => x.Id == Input.DepartmentId.Value && x.IsActive))
        {
            ErrorMessage = "Phòng ban không tồn tại hoặc đã bị khóa.";
            await LoadBranchesAsync();
            return Page();
        }

        if (!await _context.Branches.AnyAsync(x => x.Id == Input.BranchId && x.IsActive))
        {
            ErrorMessage = "Chi nhánh không tồn tại hoặc đã bị khóa.";
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

        if (!string.IsNullOrWhiteSpace(Input.Email) &&
            await _context.Employees.AnyAsync(x => x.Id != id && x.Email == Input.Email))
        {
            ErrorMessage = "Email đã được sử dụng.";
            await LoadBranchesAsync();
            return Page();
        }

        if (await _context.Employees.AnyAsync(x => x.Id != id && x.EmployeeCode == Input.EmployeeCode))
        {
            ErrorMessage = "Mã nhân viên đã tồn tại.";
            await LoadBranchesAsync();
            return Page();
        }

        employee.EmployeeCode = Input.EmployeeCode;
        employee.FullName = Input.FullName;
        employee.Role = Input.Role;
        employee.BranchId = Input.BranchId;
        employee.DepartmentId = Input.DepartmentId;
        employee.IsActive = Input.IsActive;

        if (!string.Equals(employee.Email, Input.Email, StringComparison.OrdinalIgnoreCase))
        {
            employee.IsEmailVerified = false;
        }

        employee.Email = Input.Email;

        bool passwordWasReset = false;

        if (!string.IsNullOrWhiteSpace(NewTemporaryPassword))
        {
            var passwordHasher = new PasswordHasher<Employee>();
            employee.PasswordHash = passwordHasher.HashPassword(employee, NewTemporaryPassword);
            passwordWasReset = true;
        }

        await _context.SaveChangesAsync();

        NewTemporaryPassword = null;

        if (passwordWasReset)
        {
            SuccessMessage = "Đã cập nhật thông tin nhân viên và reset mật khẩu tạm thành công.";
        }
        else
        {
            SuccessMessage = "Đã cập nhật thông tin nhân viên thành công.";
        }

        Input = EmployeeInput.From(employee);
        await LoadBranchesAsync();

        return Page();
    }

    private async Task LoadBranchesAsync()
    {
        Branches = await _context.Branches
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();

        Departments = await _context.Departments
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();
    }

    private static string? NormalizeEmail(string? email)
    {
        return string.IsNullOrWhiteSpace(email)
            ? null
            : email.Trim().ToLowerInvariant();
    }
}

public class EmployeeInput
{
    public string EmployeeCode { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public int BranchId { get; set; }

    public int? DepartmentId { get; set; }

    public string Role { get; set; } = "Employee";

    public bool IsActive { get; set; } = true;

    public string? Email { get; set; }

    public static EmployeeInput From(Employee x)
    {
        return new EmployeeInput
        {
            EmployeeCode = x.EmployeeCode,
            FullName = x.FullName,
            BranchId = x.BranchId,
            DepartmentId = x.DepartmentId,
            Role = x.Role,
            IsActive = x.IsActive,
            Email = x.Email
        };
    }
}