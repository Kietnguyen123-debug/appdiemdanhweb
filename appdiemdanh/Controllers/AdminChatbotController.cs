using System.Security.Claims;
using AppDiemDanh.Authorization;
using appdiemdanh.DTOs;
using appdiemdanh.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace appdiemdanh.Controllers;

[ApiController]
[Route("api/admin-chatbot")]
[Authorize(Roles = "Admin,HR,Manager")]
[RequirePermission("AdminChatbot", "Use")]
public sealed class AdminChatbotController : ControllerBase
{
    private readonly IAdminAiService _adminAiService;

    public AdminChatbotController(IAdminAiService adminAiService) => _adminAiService = adminAiService;

    [HttpGet("access")]
    public IActionResult Access() => Ok(new { allowed = true });

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequestDto request)
    {
        var rawId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("id") ?? User.FindFirstValue("AdminUserId");
        if (!int.TryParse(rawId, out var adminUserId) || adminUserId <= 0)
            return Unauthorized(new { message = "JWT không chứa AdminUserId hợp lệ." });
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { message = "Tin nhắn không được để trống." });

        var context = new UserJwtContext
        {
            EmployeeId = adminUserId,
            EmployeeCode = User.FindFirstValue("EmployeeCode") ?? "ADMIN_UNKNOWN",
            FullName = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("FullName") ?? "Quản trị viên",
            Role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role") ?? string.Empty,
            BranchId = int.TryParse(User.FindFirstValue("BranchId"), out var branchId) ? branchId : 0
        };
        return Ok(await _adminAiService.ChatAsync(request, context));
    }
}
