using AppDiemDanh.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AppDiemDanh.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : TypeFilterAttribute
{
    public RequirePermissionAttribute(string module, string action)
        : base(typeof(PermissionAuthorizationFilter))
    {
        Arguments = [module, action];
    }
}

public sealed class PermissionAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly IPermissionService _permissionService;
    private readonly string _module;
    private readonly string _action;

    public PermissionAuthorizationFilter(IPermissionService permissionService, string module, string action)
    {
        _permissionService = permissionService;
        _module = module;
        _action = action;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!await _permissionService.HasPermissionAsync(context.HttpContext.User, _module, _action, context.HttpContext.RequestAborted))
            context.Result = context.HttpContext.User.Identity?.IsAuthenticated == true
                ? new ForbidResult()
                : new UnauthorizedResult();
    }
}
