using AppDiemDanh.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AppDiemDanh.Pages.Admin;

public abstract class AdminPageModel : PageModel
{
    protected bool IsAdminSession => HttpContext.Session.GetInt32("AdminEmployeeId").HasValue
        && HttpContext.Session.GetString("AdminRole") is "Admin" or "HR" or "Manager";
    protected bool IsSystemAdmin => string.Equals(HttpContext.Session.GetString("AdminRole"), "Admin", StringComparison.OrdinalIgnoreCase);
    protected int CurrentEmployeeId => HttpContext.Session.GetInt32("AdminEmployeeId") ?? 0;

    private IPermissionService? _permissionService;
    protected IPermissionService PermissionService => _permissionService ??= HttpContext.RequestServices.GetRequiredService<IPermissionService>();

    public IActionResult RequireAdmin()
    {
        if (!IsAdminSession)
            return RedirectToPage("/Admin/Login");
        return Page();
    }

    protected async Task<bool> HasPermissionAsync(string module, string action)
    {
        return await PermissionService.HasPermissionAsync(CurrentEmployeeId, module, action);
    }
}
