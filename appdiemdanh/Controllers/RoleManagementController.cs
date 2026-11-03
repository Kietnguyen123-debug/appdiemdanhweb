using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Controllers;

[Authorize(Roles = "Admin,ADMIN,HR,Manager")]
[ApiController]
[Route("api/role-management")]
public sealed class RoleManagementController : ControllerBase
{
    private readonly AppDbContext _context;

    public RoleManagementController(AppDbContext context) => _context = context;

    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyList<RoleSummaryResponse>>> Roles(CancellationToken cancellationToken)
    {
        return Ok(await _context.Roles.AsNoTracking().OrderBy(x => x.IsSystemDefault ? 0 : 1).ThenBy(x => x.Name)
            .Select(x => new RoleSummaryResponse { Id = x.Id, Name = x.Name, Description = x.Description, IsSystemDefault = x.IsSystemDefault })
            .ToListAsync(cancellationToken));
    }

    [HttpGet("matrix/{roleId:int}")]
    public async Task<ActionResult<PermissionMatrixResponse>> Matrix(int roleId, CancellationToken cancellationToken)
    {
        var role = await _context.Roles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == roleId, cancellationToken);
        if (role == null) return NotFound(new { message = "Role không tồn tại." });
        var assigned = (await _context.RolePermissions.AsNoTracking().Where(x => x.RoleId == roleId).Select(x => x.PermissionId).ToListAsync(cancellationToken)).ToHashSet();
        var permissions = await _context.Permissions.AsNoTracking().OrderBy(x => x.Module).ThenBy(x => x.Action).ToListAsync(cancellationToken);
        return Ok(new PermissionMatrixResponse
        {
            Role = new RoleSummaryResponse { Id = role.Id, Name = role.Name, Description = role.Description, IsSystemDefault = role.IsSystemDefault },
            Permissions = permissions.Select(x => new PermissionMatrixItem { Id = x.Id, Module = x.Module, Action = x.Action, Description = x.Description, Enabled = assigned.Contains(x.Id) }).ToList()
        });
    }

    [Authorize(Roles = "Admin,ADMIN")]
    [HttpPost("roles")]
    public async Task<ActionResult<RoleSummaryResponse>> CreateRole([FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var description = request.Description?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || string.IsNullOrWhiteSpace(description) || description.Length > 500) return BadRequest(new { message = "Tên role và mô tả là bắt buộc; tên tối đa 100, mô tả tối đa 500 ký tự." });
        if (await _context.Roles.AnyAsync(x => x.Name == name, cancellationToken)) return Conflict(new { message = "Tên role đã tồn tại." });
        var role = new Role { Name = name, Description = description, IsSystemDefault = false };
        _context.Roles.Add(role);
        await _context.SaveChangesAsync(cancellationToken);
        return Ok(new RoleSummaryResponse { Id = role.Id, Name = role.Name, Description = role.Description, IsSystemDefault = false });
    }

    [Authorize(Roles = "Admin,ADMIN")]
    [HttpPut("roles/{roleId:int}")]
    public async Task<ActionResult<RoleSummaryResponse>> UpdateRole(int roleId, [FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await _context.Roles.SingleOrDefaultAsync(x => x.Id == roleId, cancellationToken);
        var name = request.Name?.Trim();
        var description = request.Description?.Trim();
        if (role == null) return NotFound(new { message = "Role không tồn tại." });
        if (role.IsSystemDefault) return BadRequest(new { message = "Không thể sửa role hệ thống." });
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || string.IsNullOrWhiteSpace(description) || description.Length > 500) return BadRequest(new { message = "Tên role và mô tả là bắt buộc." });
        if (await _context.Roles.AnyAsync(x => x.Id != roleId && x.Name == name, cancellationToken)) return Conflict(new { message = "Tên role đã tồn tại." });
        role.Name = name;
        role.Description = description;
        await _context.SaveChangesAsync(cancellationToken);
        return Ok(new RoleSummaryResponse { Id = role.Id, Name = role.Name, Description = role.Description, IsSystemDefault = false });
    }

    [Authorize(Roles = "Admin,ADMIN")]
    [HttpDelete("roles/{roleId:int}")]
    public async Task<IActionResult> DeleteRole(int roleId, [FromQuery] string confirmation, CancellationToken cancellationToken)
    {
        var role = await _context.Roles.SingleOrDefaultAsync(x => x.Id == roleId, cancellationToken);
        if (role == null) return NotFound(new { message = "Role không tồn tại." });
        if (role.IsSystemDefault) return BadRequest(new { message = "Không thể xóa role hệ thống." });
        if (!string.Equals(confirmation, "DELETE", StringComparison.Ordinal)) return BadRequest(new { message = "Thiếu xác nhận xóa role." });
        _context.Roles.Remove(role);
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = "Admin,ADMIN")]
    [HttpPut("roles/{roleId:int}/permissions")]
    public async Task<IActionResult> UpdatePermissions(int roleId, [FromBody] UpdateRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        var roleExists = await _context.Roles.AnyAsync(x => x.Id == roleId, cancellationToken);
        if (!roleExists) return NotFound(new { message = "Role không tồn tại." });
        var permissionIds = request.PermissionIds.Distinct().ToHashSet();
        var validIds = (await _context.Permissions.Where(x => permissionIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken)).ToHashSet();
        if (validIds.Count != permissionIds.Count) return BadRequest(new { message = "Danh sách quyền không hợp lệ." });

        var current = await _context.RolePermissions.Where(x => x.RoleId == roleId).ToListAsync(cancellationToken);
        _context.RolePermissions.RemoveRange(current.Where(x => !permissionIds.Contains(x.PermissionId)));
        var currentIds = current.Select(x => x.PermissionId).ToHashSet();
        _context.RolePermissions.AddRange(validIds.Where(x => !currentIds.Contains(x)).Select(x => new RolePermission { RoleId = roleId, PermissionId = x }));
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = "Admin,ADMIN")]
    [HttpPut("employees/{employeeId:int}/role")]
    public async Task<IActionResult> AssignRole(int employeeId, [FromBody] AssignRoleRequest request, CancellationToken cancellationToken)
    {
        var employee = await _context.Employees.SingleOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        var role = await _context.Roles.SingleOrDefaultAsync(x => x.Id == request.RoleId, cancellationToken);
        if (employee == null || role == null) return NotFound(new { message = "Employee hoặc role không tồn tại." });
        employee.RoleId = role.Id;
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
