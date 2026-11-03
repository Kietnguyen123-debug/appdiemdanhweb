using System.Net;

namespace AppDiemDanh.Extensions;

public static class IpHelper
{
    /// <summary>
    /// Chuẩn hóa chuỗi IP từ request hoặc database về một định dạng thống nhất.
    /// </summary>
    public static string NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return string.Empty;

        var clean = ip.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
        clean = clean.Trim();

        if (clean.StartsWith("[", StringComparison.Ordinal) && clean.Contains(']'))
        {
            clean = clean[1..clean.IndexOf(']')];
        }
        else if (clean.Count(value => value == ':') == 1)
        {
            var colonIndex = clean.LastIndexOf(':');
            if (int.TryParse(clean[(colonIndex + 1)..], out _))
                clean = clean[..colonIndex];
        }

        if (clean.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
            clean = clean[7..].Trim();

        if (clean == "::1")
            clean = "127.0.0.1";

        if (IPAddress.TryParse(clean, out var address))
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            if (IPAddress.IsLoopback(address))
                return "127.0.0.1";

            return address.ToString();
        }

        return clean.Trim();
    }
}
