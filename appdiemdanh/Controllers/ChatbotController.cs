using appdiemdanh.DTOs;
using appdiemdanh.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace appdiemdanh.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ChatbotController : ControllerBase
    {
        private readonly IGeminiChatService _geminiChatService;

        public ChatbotController(IGeminiChatService geminiChatService)
        {
            _geminiChatService = geminiChatService;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ChatRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new ChatResponseDto { Success = false, Reply = "Tin nhắn không được để trống." });
            }

            // 1. Ép kiểu an toàn bằng TryParse tránh văng Exception 500 khi JWT Claim bất thường
            string rawEmployeeId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                   ?? User.FindFirst("id")?.Value
                                   ?? User.FindFirst("EmployeeId")?.Value
                                   ?? "0";
            int.TryParse(rawEmployeeId, out int employeeId);

            string rawBranchId = User.FindFirst("BranchId")?.Value ?? "1";
            int.TryParse(rawBranchId, out int branchId);

            // 2. Trích xuất User Context từ JWT Token
            var userContext = new UserJwtContext
            {
                EmployeeId = employeeId,
                EmployeeCode = User.FindFirst("EmployeeCode")?.Value ?? "NV_UNKNOWN",
                FullName = User.FindFirst(ClaimTypes.Name)?.Value
                           ?? User.FindFirst("FullName")?.Value
                           ?? "Nhân viên",
                Role = User.FindFirst(ClaimTypes.Role)?.Value
                       ?? User.FindFirst("role")?.Value
                       ?? "Employee",
                BranchId = branchId == 0 ? 1 : branchId
            };

            // 3. Kiểm tra tính hợp lệ của Token
            if (userContext.EmployeeId == 0)
            {
                return Unauthorized(new ChatResponseDto { Success = false, Reply = "Xác thực JWT thất bại, không tìm thấy EmployeeId hợp lệ." });
            }

            // 4. Xử lý yêu cầu qua Gemini AI Service
            var result = await _geminiChatService.ChatAsync(request, userContext);
            return Ok(result);
        }
    }
}