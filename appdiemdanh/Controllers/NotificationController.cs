using AppDiemDanh.DTOs;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly IFcmService _fcmService;
        private readonly AppDbContext _context;

        public NotificationController(IFcmService fcmService, AppDbContext context)
        {
            _fcmService = fcmService;
            _context = context;
        }

        [HttpGet("unread-count")]
        public async Task<ActionResult<CountResponse>> UnreadCount()
        {
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized();
            return Ok(new CountResponse { Count = await _context.NotificationLogs.CountAsync(x => x.EmployeeId == employeeId && !x.IsRead) });
        }

        [HttpPut("read-all")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> ReadAll()
        {
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized();
            await _context.NotificationLogs.Where(x => x.EmployeeId == employeeId && !x.IsRead).ExecuteUpdateAsync(x => x.SetProperty(n => n.IsRead, true));
            return NoContent();
        }

        [HttpDelete("unregister-token")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> UnregisterToken([FromQuery] string? deviceId)
        {
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized();
            var query = _context.EmployeeFcmTokens.Where(x => x.EmployeeId == employeeId);
            if (!string.IsNullOrWhiteSpace(deviceId)) query = query.Where(x => x.DeviceId == deviceId.Trim());
            await query.ExecuteDeleteAsync();
            return NoContent();
        }

        [HttpPost("register-token")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<MessageResponse>> RegisterToken([FromBody] RegisterFcmTokenRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.FcmToken))
                return BadRequest(new { message = "FCM token không được để trống." });
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized(new { message = "Token không hợp lệ." });
            try
            {
                await _fcmService.RegisterTokenAsync(employeeId, request);
                return Ok(new MessageResponse { Message = "Đăng ký FCM token thành công." });
            }
            catch (ArgumentException exception)
            {
                return BadRequest(new { message = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return NotFound(new { message = exception.Message });
            }
        }

        [HttpGet("my-notifications")]
        [ProducesResponseType(typeof(NotificationPageDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<NotificationPageDto>> GetMyNotifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (page < 1 || pageSize < 1 || pageSize > 100) return BadRequest(new { message = "Phân trang không hợp lệ." });
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized(new { message = "Token không hợp lệ." });
            return Ok(await _fcmService.GetMyNotificationsAsync(employeeId, page, pageSize));
        }

        [HttpPut("{id:long}/read")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<MessageResponse>> MarkAsRead(long id)
        {
            if (id <= 0) return BadRequest(new { message = "Mã thông báo không hợp lệ." });
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized(new { message = "Token không hợp lệ." });
            bool marked = await _fcmService.MarkAsReadAsync(employeeId, id);
            return marked
                ? Ok(new MessageResponse { Message = "Đã đánh dấu thông báo đã đọc." })
                : NotFound(new { message = "Không tìm thấy thông báo." });
        }

        private bool TryGetCurrentEmployeeId(out int employeeId)
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            return int.TryParse(claim?.Value, out employeeId) && employeeId > 0;
        }
    }
}
