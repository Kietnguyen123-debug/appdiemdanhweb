using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AppDiemDanh.Services;

public sealed record PermissionDefinition(string Module, string Action, string Description);

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(ClaimsPrincipal user, string module, string action, CancellationToken cancellationToken = default);
    Task<bool> HasPermissionAsync(int employeeId, string module, string action, CancellationToken cancellationToken = default);
    Task SeedAsync(CancellationToken cancellationToken = default);
}

public sealed class PermissionService : IPermissionService
{
    public static readonly IReadOnlyList<PermissionDefinition> Catalog =
    [
        new("Dashboard", "View", "Xem tổng quan dashboard"),
        new("Employees", "View", "Xem nhân viên"),
        new("Employees", "Create", "Thêm nhân viên"),
        new("Employees", "Edit", "Sửa nhân viên"),
        new("Employees", "Delete", "Xóa nhân viên"),
        new("Departments", "View", "Xem phòng ban"),
        new("Departments", "Create", "Thêm phòng ban"),
        new("Departments", "Edit", "Sửa phòng ban"),
        new("Departments", "Delete", "Xóa phòng ban"),
        new("Branches", "View", "Xem chi nhánh"),
        new("Branches", "Create", "Thêm chi nhánh"),
        new("Branches", "Edit", "Sửa chi nhánh"),
        new("Branches", "Delete", "Xóa chi nhánh"),
        new("Attendance", "View", "Xem chấm công"),
        new("Attendance", "Edit", "Điều chỉnh chấm công"),
        new("Shifts", "View", "Xem ca làm việc"),
        new("Shifts", "Create", "Thêm ca làm việc"),
        new("Shifts", "Edit", "Sửa ca làm việc"),
        new("Shifts", "Delete", "Xóa ca làm việc"),
        new("Leave", "View", "Xem nghỉ phép"),
        new("Leave", "Approve", "Duyệt nghỉ phép"),
        new("Explanations", "View", "Xem giải trình"),
        new("Explanations", "Approve", "Duyệt giải trình"),
        new("Network", "View", "Xem cấu hình mạng"),
        new("Network", "Create", "Thêm mạng/IP"),
        new("Network", "Edit", "Sửa cấu hình mạng"),
        new("Network", "Delete", "Xóa cấu hình mạng"),
        new("Payroll", "View", "Xem bảng lương"),
        new("Payroll", "Create", "Tính bảng lương"),
        new("Payroll", "Approve", "Chốt bảng lương"),
        new("SalaryConfig", "View", "Xem cấu hình lương"),
        new("SalaryConfig", "Edit", "Sửa cấu hình lương"),
        new("Overtime", "View", "Xem làm thêm giờ"),
        new("Overtime", "Approve", "Duyệt làm thêm giờ"),
        new("Advances", "View", "Xem tạm ứng lương"),
        new("Advances", "Approve", "Duyệt tạm ứng lương"),
        new("Notifications", "View", "Xem thông báo"),
        new("Notifications", "Create", "Tạo thông báo"),
        new("Reports", "View", "Xem báo cáo"),
        new("Reports", "Export", "Xuất dữ liệu báo cáo"),
        new("Settings", "View", "Xem cấu hình hệ thống"),
        new("Settings", "Edit", "Sửa cấu hình hệ thống"),
        new("AdminChatbot", "Use", "Sử dụng trợ lý AI quản trị")
    ];

    private readonly AppDbContext _context;

    public PermissionService(AppDbContext context) => _context = context;

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string module, string action, CancellationToken cancellationToken = default)
    {
        if (user.Identity?.IsAuthenticated != true) return false;
        if (user.IsInRole("Admin")) return true;
        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)) return false;
        return await HasPermissionAsync(employeeId, module, action, cancellationToken);
    }

    public async Task<bool> HasPermissionAsync(int employeeId, string module, string action, CancellationToken cancellationToken = default)
    {
        if (employeeId <= 0) return false;
        var employee = await _context.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId && x.IsActive)
            .Select(x => new { x.RoleId, x.Role })
            .SingleOrDefaultAsync(cancellationToken);
        if (employee == null) return false;
        if (string.Equals(employee.Role, "Admin", StringComparison.OrdinalIgnoreCase)) return true;

        var roleId = employee.RoleId ?? await _context.Roles
            .Where(x => x.Name == employee.Role)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return roleId.HasValue && await _context.RolePermissions.AsNoTracking()
            .AnyAsync(x => x.RoleId == roleId.Value && x.Permission.Module == module && x.Permission.Action == action, cancellationToken);
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var systemRoles = new[]
        {
            new { Name = "Admin", Description = "Quản trị hệ thống", IsSystemDefault = true },
            new { Name = "HR", Description = "Quản trị nhân sự", IsSystemDefault = true },
            new { Name = "Manager", Description = "Quản lý chi nhánh", IsSystemDefault = true },
            new { Name = "Employee", Description = "Nhân viên", IsSystemDefault = true }
        };

        foreach (var definition in systemRoles)
        {
            if (!await _context.Roles.AnyAsync(x => x.Name == definition.Name, cancellationToken))
                _context.Roles.Add(new Role { Name = definition.Name, Description = definition.Description, IsSystemDefault = definition.IsSystemDefault });
        }
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var definition in Catalog)
        {
            if (!await _context.Permissions.AnyAsync(x => x.Module == definition.Module && x.Action == definition.Action, cancellationToken))
                _context.Permissions.Add(new Permission { Module = definition.Module, Action = definition.Action, Description = definition.Description });
        }
        await _context.SaveChangesAsync(cancellationToken);

        var adminRole = await _context.Roles.SingleAsync(x => x.Name == "Admin", cancellationToken);
        var permissions = await _context.Permissions.ToListAsync(cancellationToken);
        var assigned = (await _context.RolePermissions.Where(x => x.RoleId == adminRole.Id).Select(x => x.PermissionId).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var permission in permissions.Where(x => !assigned.Contains(x.Id)))
            _context.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permission.Id });

        var managerRole = await _context.Roles.SingleAsync(x => x.Name == "Manager", cancellationToken);
        var managerPermissions = new[]
        {
            "Dashboard:View",
            "Employees:View",
            "Departments:View",
            "Branches:View",
            "Attendance:View",
            "Shifts:View",
            "Leave:View", "Leave:Approve",
            "Explanations:View", "Explanations:Approve",
            "Network:View",
            "Payroll:View",
            "Overtime:View", "Overtime:Approve",
            "Advances:View", "Advances:Approve",
            "Notifications:View", "Notifications:Create",
            "Reports:View", "Reports:Export",
            "AdminChatbot:Use"
        };
        var managerAssigned = (await _context.RolePermissions.Where(x => x.RoleId == managerRole.Id).Select(x => x.PermissionId).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var permKey in managerPermissions)
        {
            var parts = permKey.Split(':');
            var module = parts[0];
            var action = parts[1];
            var permission = permissions.FirstOrDefault(x => x.Module == module && x.Action == action);
            if (permission != null && !managerAssigned.Contains(permission.Id))
                _context.RolePermissions.Add(new RolePermission { RoleId = managerRole.Id, PermissionId = permission.Id });
        }

        var hrRole = await _context.Roles.SingleAsync(x => x.Name == "HR", cancellationToken);
        var hrPermissions = new[]
        {
            "Dashboard:View",
            "Employees:View", "Employees:Create", "Employees:Edit",
            "Departments:View",
            "Branches:View", "Branches:Create", "Branches:Edit",
            "Attendance:View",
            "Shifts:View",
            "Leave:View",
            "Explanations:View",
            "Payroll:View",
            "Overtime:View",
            "Advances:View",
            "Notifications:View", "Notifications:Create",
            "Reports:View", "Reports:Export",
            "AdminChatbot:Use"
        };
        var hrAssigned = (await _context.RolePermissions.Where(x => x.RoleId == hrRole.Id).Select(x => x.PermissionId).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var permKey in hrPermissions)
        {
            var parts = permKey.Split(':');
            var module = parts[0];
            var action = parts[1];
            var permission = permissions.FirstOrDefault(x => x.Module == module && x.Action == action);
            if (permission != null && !hrAssigned.Contains(permission.Id))
                _context.RolePermissions.Add(new RolePermission { RoleId = hrRole.Id, PermissionId = permission.Id });
        }
        await _context.SaveChangesAsync(cancellationToken);

        var roles = await _context.Roles.ToDictionaryAsync(x => x.Name, cancellationToken);
        var employees = await _context.Employees.Where(x => x.RoleId == null).ToListAsync(cancellationToken);
        foreach (var employee in employees)
            if (roles.TryGetValue(employee.Role, out var role)) employee.RoleId = role.Id;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public static bool IsAdmin(string? role) => string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
}
