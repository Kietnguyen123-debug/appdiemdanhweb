using AppDiemDanh.Models;
using AppDiemDanh.Services;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class PermissionsModel : AdminPageModel
{
    private readonly AppDbContext _context;

    public PermissionsModel(AppDbContext context) => _context = context;

    public IReadOnlyList<Role> Roles { get; private set; } = [];
    public IReadOnlyList<Permission> Permissions { get; private set; } = [];
    public int SelectedRoleId { get; private set; }
    public HashSet<int> SelectedPermissionIds { get; private set; } = [];
    public bool IsSelectedRoleAdmin { get; private set; }
    public string? StatusMessage { get; private set; }

    [BindProperty]
    public string NewRoleName { get; set; } = string.Empty;

    [BindProperty]
    public string NewRoleDescription { get; set; } = string.Empty;

    [BindProperty]
    public string EditRoleName { get; set; } = string.Empty;

    [BindProperty]
    public string EditRoleDescription { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int? roleId)
    {
        if (!IsSystemAdmin) return Forbid();
        await LoadAsync(roleId);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int roleId, List<int>? permissionIds)
    {
        if (!IsSystemAdmin) return Forbid();
        var role = await _context.Roles.SingleOrDefaultAsync(x => x.Id == roleId);
        if (role == null) return NotFound();
        if (string.Equals(role.Name, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            var allPermissionIds = await _context.Permissions.Select(x => x.Id).ToListAsync();
            var assignedPermissionIds = await _context.RolePermissions
                .Where(x => x.RoleId == roleId)
                .Select(x => x.PermissionId)
                .ToListAsync();
            var missingPermissionIds = allPermissionIds.Except(assignedPermissionIds).ToList();
            _context.RolePermissions.AddRange(missingPermissionIds.Select(permissionId => new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId
            }));
            if (missingPermissionIds.Count > 0) await _context.SaveChangesAsync();
            return RedirectToPage(new { roleId, saved = true });
        }
        var ids = (permissionIds ?? []).Distinct().ToHashSet();
        var validIds = (await _context.Permissions.Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToListAsync()).ToHashSet();
        if (validIds.Count != ids.Count)
        {
            ModelState.AddModelError(string.Empty, "Danh sách quyền không hợp lệ.");
            await LoadAsync(roleId);
            return Page();
        }

        var current = await _context.RolePermissions.Where(x => x.RoleId == roleId).ToListAsync();
        _context.RolePermissions.RemoveRange(current.Where(x => !ids.Contains(x.PermissionId)));
        var currentIds = current.Select(x => x.PermissionId).ToHashSet();
        _context.RolePermissions.AddRange(validIds.Where(x => !currentIds.Contains(x)).Select(x => new RolePermission { RoleId = roleId, PermissionId = x }));
        await _context.SaveChangesAsync();
        return RedirectToPage(new { roleId, saved = true });
    }

    public async Task<IActionResult> OnPostCreateRoleAsync()
    {
        if (!IsSystemAdmin) return Forbid();
        var name = NewRoleName.Trim();
        var description = NewRoleDescription.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || string.IsNullOrWhiteSpace(description) || description.Length > 500 || await _context.Roles.AnyAsync(x => x.Name == name))
        {
            ModelState.AddModelError(string.Empty, "Tên role không hợp lệ hoặc đã tồn tại.");
            await LoadAsync(null);
            return Page();
        }
        var role = new Role { Name = name, Description = description, IsSystemDefault = false };
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();
        return RedirectToPage(new { roleId = role.Id });
    }

    public async Task<IActionResult> OnPostEditRoleAsync(int roleId)
    {
        if (!IsSystemAdmin) return Forbid();
        var role = await _context.Roles.SingleOrDefaultAsync(x => x.Id == roleId);
        var name = EditRoleName.Trim();
        var description = EditRoleDescription.Trim();
        if (role == null) return NotFound();
        if (role.IsSystemDefault) return BadRequest("Không thể sửa role hệ thống.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || string.IsNullOrWhiteSpace(description) || description.Length > 500 || await _context.Roles.AnyAsync(x => x.Id != roleId && x.Name == name))
        {
            ModelState.AddModelError(string.Empty, "Tên role và mô tả là bắt buộc, đồng thời tên role không được trùng.");
            await LoadAsync(roleId);
            return Page();
        }
        role.Name = name;
        role.Description = description;
        await _context.SaveChangesAsync();
        return RedirectToPage(new { roleId, updated = true });
    }

    public async Task<IActionResult> OnPostDeleteRoleAsync(int roleId, string confirmation)
    {
        if (!IsSystemAdmin) return Forbid();
        var role = await _context.Roles.SingleOrDefaultAsync(x => x.Id == roleId);
        if (role == null) return NotFound();
        if (role.IsSystemDefault) return BadRequest("Không thể xóa role hệ thống.");
        if (!string.Equals(confirmation, "DELETE", StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "Vui lòng xác nhận xóa role.");
            await LoadAsync(roleId);
            return Page();
        }
        _context.Roles.Remove(role);
        await _context.SaveChangesAsync();
        return RedirectToPage(new { deleted = true });
    }

    private async Task LoadAsync(int? roleId)
    {
        var existingPermissionKeys = (await _context.Permissions
            .AsNoTracking()
            .Select(x => new { x.Module, x.Action })
            .ToListAsync())
            .Select(x => $"{x.Module}:{x.Action}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingPermissions = AppDiemDanh.Services.PermissionService.Catalog
            .Where(x => !existingPermissionKeys.Contains($"{x.Module}:{x.Action}"))
            .Select(x => new Permission
            {
                Module = x.Module,
                Action = x.Action,
                Description = x.Description
            })
            .ToList();

        if (missingPermissions.Count > 0)
        {
            _context.Permissions.AddRange(missingPermissions);
            await _context.SaveChangesAsync();
        }

        Roles = await _context.Roles.AsNoTracking().OrderBy(x => x.IsSystemDefault ? 0 : 1).ThenBy(x => x.Name).ToListAsync();
        SelectedRoleId = roleId ?? Roles.FirstOrDefault()?.Id ?? 0;
        if (!Roles.Any(x => x.Id == SelectedRoleId)) SelectedRoleId = Roles.FirstOrDefault()?.Id ?? 0;
        Permissions = await _context.Permissions.AsNoTracking().OrderBy(x => x.Module).ThenBy(x => x.Action).ToListAsync();
        IsSelectedRoleAdmin = string.Equals(Roles.FirstOrDefault(x => x.Id == SelectedRoleId)?.Name, "Admin", StringComparison.OrdinalIgnoreCase);
        SelectedPermissionIds = (await _context.RolePermissions.AsNoTracking().Where(x => x.RoleId == SelectedRoleId).Select(x => x.PermissionId).ToListAsync()).ToHashSet();
        if (IsSelectedRoleAdmin)
        {
            var missingPermissionIds = Permissions.Select(x => x.Id).Except(SelectedPermissionIds).ToList();
            if (missingPermissionIds.Count > 0)
            {
                _context.RolePermissions.AddRange(missingPermissionIds.Select(permissionId => new RolePermission
                {
                    RoleId = SelectedRoleId,
                    PermissionId = permissionId
                }));
                await _context.SaveChangesAsync();
            }
            SelectedPermissionIds = Permissions.Select(x => x.Id).ToHashSet();
        }
    }
}
