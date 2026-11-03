namespace AppDiemDanh.DTOs;

public sealed class RoleSummaryResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystemDefault { get; set; }
}

public sealed class PermissionMatrixResponse
{
    public RoleSummaryResponse Role { get; set; } = new();
    public List<PermissionMatrixItem> Permissions { get; set; } = [];
}

public sealed class PermissionMatrixItem
{
    public int Id { get; set; }
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class CreateRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class UpdateRolePermissionsRequest
{
    public List<int> PermissionIds { get; set; } = [];
}

public sealed class AssignRoleRequest
{
    public int RoleId { get; set; }
}
