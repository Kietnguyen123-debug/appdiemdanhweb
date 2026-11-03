using System.Net;
using AppDiemDanh.Extensions;
using Microsoft.AspNetCore.Http;

namespace AppDiemDanh.Services;

public interface IClientIpResolver
{
    string? Resolve(HttpContext context);
    bool TryNormalizePublic(string? value, out string? normalizedIp);
}

public sealed class ClientIpResolver : IClientIpResolver
{
    public string? Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (TryGetPublicIp(context.GetClientIpAddress(), out var resolvedIp)) return resolvedIp;

        foreach (var value in new[]
        {
            context.Request.Headers["CF-Connecting-IP"].FirstOrDefault(),
            context.Request.Headers["X-Real-IP"].FirstOrDefault(),
            context.Connection.RemoteIpAddress?.ToString()
        })
        {
            if (TryGetPublicIp(value, out var ip)) return ip;
        }

        return null;
    }

    public bool TryNormalizePublic(string? value, out string? normalizedIp) => TryGetPublicIp(value, out normalizedIp);

    private static bool TryGetPublicIp(string? value, out string? normalizedIp)
    {
        normalizedIp = null;
        var normalized = IpHelper.NormalizeIp(value);
        if (!IPAddress.TryParse(normalized, out var address)) return false;

        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (!IsPublic(address)) return false;

        normalizedIp = address.ToString();
        return true;
    }

    private static bool IsPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] != 10
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] == 169 && bytes[1] == 254);
        }

        var ipv6 = address.GetAddressBytes();
        return (ipv6[0] & 0xFE) != 0xFC
            && !(ipv6[0] == 0xFE && (ipv6[1] & 0xC0) == 0x80)
            && !address.IsIPv6Multicast;
    }
}
