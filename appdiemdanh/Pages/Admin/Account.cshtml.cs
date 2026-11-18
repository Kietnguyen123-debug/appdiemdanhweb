using AppDiemDanh.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class AccountModel : AdminPageModel
{
    private readonly AppDbContext _context;

    public AccountDetails? Account { get; private set; }

    public AccountModel(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsAdminSession)
            return RedirectToPage("/Admin/Login");

        Account = await _context.Employees
            .AsNoTracking()
            .Where(x => x.Id == CurrentEmployeeId && x.IsActive)
            .Select(x => new AccountDetails
            {
                EmployeeCode = x.EmployeeCode,
                FullName = x.FullName,
                Role = x.Role,
                Email = x.Email,
                BranchName = x.Branch != null ? x.Branch.Name : null,
                DepartmentName = x.Department != null ? x.Department.Name : null,
                IsActivated = x.IsActivated,
                IsEmailVerified = x.IsEmailVerified
            })
            .SingleOrDefaultAsync();

        if (Account is null)
        {
            HttpContext.Session.Clear();
            return RedirectToPage("/Admin/Login");
        }

        return Page();
    }

    public sealed class AccountDetails
    {
        public string EmployeeCode { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public string? Email { get; init; }
        public string? BranchName { get; init; }
        public string? DepartmentName { get; init; }
        public bool IsActivated { get; init; }
        public bool IsEmailVerified { get; init; }
    }
}
