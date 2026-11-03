using Microsoft.AspNetCore.Http;

namespace AppDiemDanh.Extensions;

public static class HttpContextIpExtensions
{
    public static string GetClientIpAddress(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var candidate = IpHelper.NormalizeIp(forwardedFor);

        return !string.IsNullOrEmpty(candidate)
            ? candidate
            : IpHelper.NormalizeIp(context.Connection.RemoteIpAddress?.ToString());
    }

    public static string? NormalizeIp(string? value)
    {
        var normalized = IpHelper.NormalizeIp(value);
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
