using System.Security.Claims;
using AppDiemDanh.DTOs;
using AppDiemDanh.Services;
using AppDiemDanh.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class LeaveController : ControllerBase
    {
        private readonly ILeaveService _leaveService;
        private readonly AppDbContext _context;

        public LeaveController(ILeaveService leaveService, AppDbContext context) { _leaveService = leaveService; _context = context; }

        [Authorize(Roles = "Admin,HR,Manager")]
        [HttpGet("admin")]
        [ProducesResponseType(typeof(PagedResponse<AdminLeaveDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> AdminList([FromQuery] int? branchId, [FromQuery] int? employeeId, [FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (page < 1 || pageSize is < 1 or > 100 || branchId is <= 0 || employeeId is <= 0 || from > to) return BadRequest();
            if (User.IsInRole("Manager"))
            {
                if (!int.TryParse(User.FindFirstValue("BranchId"), out var ownBranch)) return Forbid();
                if (branchId.HasValue && branchId != ownBranch) return Forbid();
                branchId = ownBranch;
            }
            var query = _context.LeaveRequests.AsNoTracking().Where(x => (!branchId.HasValue || x.Employee!.BranchId == branchId) && (!employeeId.HasValue || x.EmployeeId == employeeId) && (string.IsNullOrWhiteSpace(status) || x.Status == status) && (!from.HasValue || x.EndDate >= from) && (!to.HasValue || x.StartDate <= to)).Select(x => new AdminLeaveDto { Id = x.Id, EmployeeId = x.EmployeeId, EmployeeCode = x.Employee!.EmployeeCode, EmployeeName = x.Employee.FullName, BranchId = x.Employee.BranchId, StartDate = x.StartDate, EndDate = x.EndDate, LeaveType = x.LeaveType, Reason = x.Reason, Status = x.Status, ReviewerId = x.ApprovedByEmployeeId, ReviewNote = x.RejectReason, CreatedAt = x.CreatedAt });
            return Ok(await PageAsync(query, page, pageSize));
        }

        [Authorize(Roles = "Admin,HR,Manager")]
        [HttpGet("admin/pending")]
        [ProducesResponseType(typeof(PagedResponse<AdminLeaveDto>), StatusCodes.Status200OK)]
        public Task<IActionResult> AdminPending([FromQuery] int? branchId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => AdminList(branchId, null, "PENDING", null, null, page, pageSize);

        private async Task<PagedResponse<T>> PageAsync<T>(IQueryable<T> query, int page, int pageSize) { var total = await query.CountAsync(); return new PagedResponse<T> { Items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), Page = page, PageSize = pageSize, TotalItems = total, TotalPages = (int)Math.Ceiling(total / (double)pageSize) }; }

        [HttpGet("my-requests")]
        [ProducesResponseType(typeof(IReadOnlyList<EmployeeLeaveDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyRequests()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)) return Unauthorized();
            return Ok(await _context.LeaveRequests.AsNoTracking().Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.CreatedAt)
                .Select(x => new EmployeeLeaveDto { Id = x.Id, StartDate = x.StartDate, EndDate = x.EndDate, LeaveType = x.LeaveType, Reason = x.Reason, Status = x.Status, IsPaidLeave = x.IsPaidLeave, RejectReason = x.RejectReason, CreatedAt = x.CreatedAt }).ToListAsync());
        }

        [HttpGet("{id:long}")]
        [ProducesResponseType(typeof(EmployeeLeaveDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetById(long id)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)) return Unauthorized();
            var item = await _context.LeaveRequests.AsNoTracking().Where(x => x.Id == id && x.EmployeeId == employeeId)
                .Select(x => new EmployeeLeaveDto { Id = x.Id, StartDate = x.StartDate, EndDate = x.EndDate, LeaveType = x.LeaveType, Reason = x.Reason, Status = x.Status, IsPaidLeave = x.IsPaidLeave, RejectReason = x.RejectReason, CreatedAt = x.CreatedAt }).FirstOrDefaultAsync();
            return item == null ? NotFound() : Ok(item);
        }

        [HttpPut("{id:long}/cancel")]
        [ProducesResponseType(typeof(CancelRequestResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Cancel(long id)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)) return Unauthorized();
            var item = await _context.LeaveRequests.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
            if (item == null) return NotFound();
            if (item.Status != "PENDING") return Conflict(new { message = "Chỉ có thể hủy đơn đang chờ duyệt." });
            item.Status = "CANCELLED"; await _context.SaveChangesAsync();
            return Ok(new CancelRequestResponse { Id = id, Status = item.Status });
        }

        // Nhân viên tạo đơn xin nghỉ
        [HttpPost("request")]
        [ProducesResponseType(typeof(LeaveRequestCreatedResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateRequest([FromBody] CreateLeaveRequest request)
        {
            if (request == null) return BadRequest(new { message = "Thông tin đơn nghỉ không được để trống." });
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId) || employeeId <= 0)
                return Unauthorized(new { message = "Token không hợp lệ." });

            var leave = await _leaveService.CreateLeaveRequestAsync(employeeId, request);
            if (leave == null)
                return BadRequest(new { message = "Khoảng thời gian nghỉ không hợp lệ hoặc không tìm thấy nhân viên." });

            return Ok(new LeaveRequestCreatedResponse
            {
                Message = "Đã gửi đơn xin nghỉ phép thành công",
                Id = leave.Id,
                IsPaidLeave = leave.IsPaidLeave,
                StartDate = leave.StartDate,
                EndDate = leave.EndDate
            });
        }

        [HttpGet("my-balance")]
        [ProducesResponseType(typeof(LeaveBalanceDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyBalance()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId) || employeeId <= 0)
                return Unauthorized(new { message = "Token không hợp lệ." });
            var balance = await _leaveService.GetBalanceAsync(employeeId);
            return balance == null ? NotFound("Không tìm thấy nhân viên") : Ok(balance);
        }

        // Quản lý duyệt hoặc từ chối đơn nghỉ phép
        [Authorize(Roles = "Admin,Manager")]
        [HttpPut("approve/{id}")]
        [ProducesResponseType(typeof(LeaveApprovalResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> ApproveRequest(long id, [FromQuery] bool isApproved, [FromQuery] string? rejectReason)
        {
            if (id <= 0) return BadRequest(new { message = "Mã đơn nghỉ không hợp lệ." });
            if (!isApproved && string.IsNullOrWhiteSpace(rejectReason)) return BadRequest(new { message = "Cần nhập lý do từ chối." });
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var reviewerId) || reviewerId <= 0)
                return Unauthorized(new { message = "Token không hợp lệ." });
            if (User.IsInRole("Manager"))
            {
                if (!int.TryParse(User.FindFirstValue("BranchId"), out var managerBranch))
                    return Forbid();
                var requestBranch = await _context.LeaveRequests
                    .Where(x => x.Id == id)
                    .Select(x => (int?)x.Employee!.BranchId)
                    .FirstOrDefaultAsync();
                if (!requestBranch.HasValue) return NotFound();
                if (requestBranch.Value != managerBranch) return Forbid();
            }
            var result = await _leaveService.ApproveLeaveAsync(id, reviewerId, isApproved, rejectReason);
            if (!result.Success)
                return BadRequest(new { message = result.Message });

            return Ok(new { message = result.Message, status = result.Status });
        }
    }
}