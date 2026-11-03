using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppDiemDanh.Services;

namespace AppDiemDanh.Pages.Admin;

public class EmployeesModel : AdminPageModel
{
    private readonly AppDbContext _context;
    private readonly PasswordHasher<string> _hasher = new();
    private readonly IAccountSecurityService _accountSecurityService;

    public List<Employee> Employees { get; private set; } = [];
    public List<Branch> Branches { get; private set; } = [];
    public List<Department> Departments { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Role { get; set; }
    [BindProperty(SupportsGet = true)] public string? Activation { get; set; }
    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public int? DepartmentId { get; set; }

    [BindProperty] public CreateEmployeeAccountRequest Create { get; set; } = new();

    public string? Message { get; set; }

    public int TotalAccounts { get; private set; }
    public int ActivatedAccounts { get; private set; }
    public int NotActivatedAccounts { get; private set; }

    public EmployeesModel(AppDbContext context, IAccountSecurityService accountSecurityService)
    {
        _context = context;
        _accountSecurityService = accountSecurityService;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!await HasPermissionAsync("Employees", "View")) return Forbid();
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!await HasPermissionAsync("Employees", "Create")) return Forbid();
        Create ??= new();
        Create.EmployeeCode = Create.EmployeeCode?.Trim() ?? string.Empty;
        Create.FullName = Create.FullName?.Trim() ?? string.Empty;
        Create.Role = Create.Role?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(Create.EmployeeCode) || string.IsNullOrWhiteSpace(Create.FullName) ||
            string.IsNullOrWhiteSpace(Create.TemporaryPassword) || Create.TemporaryPassword.Length < 6 ||
            Create.BranchId <= 0 || Create.Role is not ("Admin" or "HR" or "Manager" or "Employee"))
        {
            Message = "Mã nhân viên, họ tên, mật khẩu, vai trò và chi nhánh phải hợp lệ.";
            await LoadAsync();
            return Page();
        }

        if (!await _context.Branches.AnyAsync(x => x.Id == Create.BranchId && x.IsActive))
        {
            Message = "Chi nhánh không tồn tại hoặc đã bị khóa.";
            await LoadAsync();
            return Page();
        }

        if (await _context.Employees.AnyAsync(x => x.EmployeeCode == Create.EmployeeCode))
        {
            Message = "Mã nhân viên đã tồn tại.";
            await LoadAsync();
            return Page();
        }

        _context.Employees.Add(new Employee
        {
            EmployeeCode = Create.EmployeeCode.Trim(),
            FullName = Create.FullName.Trim(),
            BranchId = Create.BranchId,
            Role = Create.Role,
            PasswordHash = _hasher.HashPassword(Create.EmployeeCode, Create.TemporaryPassword),
            IsActive = true,
            IsActivated = !string.Equals(Create.Role?.Trim(), "Employee", StringComparison.OrdinalIgnoreCase)
        });

        await _context.SaveChangesAsync();
        return RedirectToPage(new { Search, Role });
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!await HasPermissionAsync("Employees", "Edit")) return Forbid();
        if (id <= 0) return BadRequest("Mã nhân viên không hợp lệ.");

        var employee = await _context.Employees.FindAsync(id);
        if (employee != null)
        {
            employee.IsActive = !employee.IsActive;
            await _context.SaveChangesAsync();
        }
        return RedirectToPage(new { Search, Role, Activation, BranchId, DepartmentId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (!IsSystemAdmin) return RedirectToPage("/Admin/Login");
        if (!await HasPermissionAsync("Employees", "Delete")) return Forbid();
        if (id <= 0) return BadRequest("Mã nhân viên không hợp lệ.");

        Message = "Xóa nhân viên chưa được chuyển sang soft delete vì model hiện tại chưa có cờ xóa.";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveEmailAsync(int id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!await HasPermissionAsync("Employees", "Edit")) return Forbid();
        if (id <= 0) return BadRequest("Mã nhân viên không hợp lệ.");

        var employee = await _context.Employees.FindAsync(id);
        if (employee != null)
        {
            employee.Email = null;
            employee.IsEmailVerified = false;
            await _context.SaveChangesAsync();
        }
        return RedirectToPage(new { Search, Role, Activation, BranchId, DepartmentId });
    }

    public async Task<IActionResult> OnPostSendEmailOtpAsync(int id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!await HasPermissionAsync("Employees", "Edit")) return Forbid();
        if (id <= 0) return BadRequest("Mã nhân viên không hợp lệ.");

        try
        {
            await _accountSecurityService.SendEmailVerificationOtpAsync(id);
            Message = "Đã gửi OTP xác minh email.";
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostUnbindDeviceAsync(int id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!await HasPermissionAsync("Employees", "Edit")) return Forbid();
        if (id <= 0) return BadRequest("Mã nhân viên không hợp lệ.");

        var employee = await _context.Employees.FindAsync(id);
        if (employee != null)
        {
            employee.BoundDeviceId = null;

            await _context.SaveChangesAsync();
            Message = $"Đã hủy liên kết thiết bị cho nhân viên {employee.FullName}. Nhân viên có thể đăng nhập trên thiết bị mới.";
        }
        else
        {
            Message = "Không tìm thấy thông tin nhân viên.";
        }

        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var all = await _context.Employees.AsNoTracking().ToListAsync();
        TotalAccounts = all.Count;
        ActivatedAccounts = all.Count(x => x.IsActivated);
        NotActivatedAccounts = TotalAccounts - ActivatedAccounts;

        var query = _context.Employees.Include(x => x.Branch).Include(x => x.Department).AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(Search))
            query = query.Where(x => x.EmployeeCode.Contains(Search) || x.FullName.Contains(Search) || (x.Email != null && x.Email.Contains(Search)));
        if (!string.IsNullOrWhiteSpace(Role))
            query = query.Where(x => x.Role == Role);
        if (Activation == "activated")
            query = query.Where(x => x.IsActivated);
        if (Activation == "not-activated")
            query = query.Where(x => !x.IsActivated);
        if (BranchId.HasValue && BranchId.Value > 0)
            query = query.Where(x => x.BranchId == BranchId.Value);
        if (DepartmentId.HasValue && DepartmentId.Value > 0)
            query = query.Where(x => x.DepartmentId == DepartmentId.Value);

        Employees = await query.OrderByDescending(x => x.Id).ToListAsync();
        Branches = await _context.Branches.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        Departments = await _context.Departments.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
    }
}