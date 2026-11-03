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
    public class AttendanceExplanationController : ControllerBase
    {
        private readonly IAttendanceExplanationService _service;
        private readonly AppDbContext _context;

        public AttendanceExplanationController(IAttendanceExplanationService service, AppDbContext context)
        {
            _service = service;
            _context = context;
        }

        [HttpGet("my-requests")]
        [ProducesResponseType(typeof(IReadOnlyList<AttendanceExplanationResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyRequests()
        {
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized();
            return Ok(await _context.AttendanceExplanations.AsNoTracking().Where(x => x.EmployeeId == employeeId)
                .OrderByDescending(x => x.CreatedAt).Select(x => new AttendanceExplanationResponse
                {
                    Id = x.Id, EmployeeId = x.EmployeeId, TargetDate = x.TargetDate, ExplanationReason = x.ExplanationReason,
                    MissingCheckInTime = x.MissingCheckInTime, MissingCheckOutTime = x.MissingCheckOutTime, Status = x.Status,
                    ReviewerId = x.ReviewerId, ReviewNote = x.ReviewNote, CreatedAt = x.CreatedAt
                }).ToListAsync());
        }

        [HttpGet("{id:long}")]
        [ProducesResponseType(typeof(AttendanceExplanationResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetById(long id)
        {
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized();
            var item = await _context.AttendanceExplanations.AsNoTracking().Where(x => x.Id == id && x.EmployeeId == employeeId)
                .Select(x => new AttendanceExplanationResponse { Id = x.Id, EmployeeId = x.EmployeeId, TargetDate = x.TargetDate, ExplanationReason = x.ExplanationReason, MissingCheckInTime = x.MissingCheckInTime, MissingCheckOutTime = x.MissingCheckOutTime, Status = x.Status, ReviewerId = x.ReviewerId, ReviewNote = x.ReviewNote, CreatedAt = x.CreatedAt }).FirstOrDefaultAsync();
            return item == null ? NotFound() : Ok(item);
        }

        [HttpPut("{id:long}/cancel")]
        [ProducesResponseType(typeof(CancelRequestResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Cancel(long id)
        {
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized();
            var item = await _context.AttendanceExplanations.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
            if (item == null) return NotFound();
            if (item.Status != "PENDING") return Conflict(new { message = "Chỉ có thể hủy yêu cầu đang chờ duyệt." });
            item.Status = "CANCELLED";
            await _context.SaveChangesAsync();
            return Ok(new CancelRequestResponse { Id = id, Status = item.Status });
        }

        [HttpPost]
        [ProducesResponseType(typeof(AttendanceExplanationResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Submit([FromBody] SubmitAttendanceExplanationRequest request)
        {
            if (request == null || request.TargetDate == default || string.IsNullOrWhiteSpace(request.ExplanationReason))
                return BadRequest(new { message = "Ngày và nội dung giải trình không được để trống." });
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized(new { message = "Token không hợp lệ." });
            var explanation = await _service.SubmitExplanationAsync(employeeId, request);
            if (explanation == null)
                return BadRequest(new { message = "Thông tin giải trình không hợp lệ hoặc đã có giải trình đang chờ duyệt cho ngày này." });

            return Ok(explanation);
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpGet("pending")]
        [ProducesResponseType(typeof(IReadOnlyList<AttendanceExplanationResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPending()
        {
            return Ok(await _service.GetPendingExplanationsAsync());
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpPut("{id:long}/review")]
        [ProducesResponseType(typeof(AttendanceExplanationResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Review(long id, [FromBody] ReviewAttendanceExplanationRequest request)
        {
            if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Status))
                return BadRequest(new { message = "Thông tin review không hợp lệ." });
            if (!TryGetCurrentEmployeeId(out var reviewerId)) return Unauthorized(new { message = "Token không hợp lệ." });
            var explanation = await _service.ReviewExplanationAsync(
                id,
                reviewerId,
                request.Status,
                request.ReviewNote);

            if (explanation == null)
                return BadRequest(new { message = "Giải trình không tồn tại, đã được xử lý hoặc dữ liệu review không hợp lệ." });

            return Ok(explanation);
        }

        private bool TryGetCurrentEmployeeId(out int employeeId)
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            return int.TryParse(claim?.Value, out employeeId) && employeeId > 0;
        }
    }
}
