using AppDiemDanh.Extensions;
using AppDiemDanh.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Controllers;

[ApiController]
[Route("api/test")]
public class TestIpController : ControllerBase
{
    private readonly AppDbContext _context;

    public TestIpController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Test so khớp Public IP của request với IP đã đăng ký cho chi nhánh.
    /// </summary>
    [HttpGet("check-ip/{branchId:int}")]
    public async Task<IActionResult> CheckIp(int branchId)
    {
        string rawXForwardedFor = Request.Headers["X-Forwarded-For"].ToString();
        string rawRemoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        string rawClientIp = !string.IsNullOrWhiteSpace(rawXForwardedFor) ? rawXForwardedFor : rawRemoteIp;
        string parsedClientIp = IpHelper.NormalizeIp(rawClientIp);

        var dbBranchIps = await _context.BranchPublicIps
            .Where(branchIp => branchIp.BranchId == branchId && branchIp.IsActive)
            .Select(branchIp => branchIp.PublicIp)
            .ToListAsync();

        var normalizedDbIps = dbBranchIps
            .Select(IpHelper.NormalizeIp)
            .ToList();
        bool isMatched = normalizedDbIps.Any(ip =>
            string.Equals(ip, parsedClientIp, StringComparison.OrdinalIgnoreCase));

        return Ok(new
        {
            Success = true,
            IsMatched = isMatched,
            Diagnostics = new
            {
                RawXForwardedFor = rawXForwardedFor,
                RawRemoteIp = rawRemoteIp,
                ParsedClientIp = parsedClientIp,
                BranchIpsInDbRaw = dbBranchIps,
                BranchIpsInDbNormalized = normalizedDbIps
            }
        });
    }
}
