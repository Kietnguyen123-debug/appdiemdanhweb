using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using AppDiemDanh.Services;
using AppDiemDanh.Services.Face;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Linq;

namespace AppDiemDanh.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AttendanceController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IAttendanceService _attendanceService;
        private readonly IClientIpResolver _clientIpResolver;

        public AttendanceController(AppDbContext context, IAttendanceService attendanceService, IClientIpResolver clientIpResolver)
        {
            _context = context;
            _attendanceService = attendanceService;
            _clientIpResolver = clientIpResolver;
        }

        [HttpGet("my-today")]
        public async Task<ActionResult<AttendanceItemDto>> GetMyToday()
        {
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized();
            var item = await _context.Attendances.AsNoTracking().Where(x => x.EmployeeId == employeeId && x.WorkDate == DateTime.Today)
                .Select(x => ToDto(x)).FirstOrDefaultAsync();
            return item == null ? NotFound() : Ok(item);
        }

        [HttpGet("my-history")]
        public async Task<ActionResult<IReadOnlyList<AttendanceItemDto>>> GetMyHistory([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized();
            var query = _context.Attendances.AsNoTracking().Where(x => x.EmployeeId == employeeId);
            if (from.HasValue) query = query.Where(x => x.WorkDate >= from.Value.Date);
            if (to.HasValue) query = query.Where(x => x.WorkDate <= to.Value.Date);
            return Ok(await query.OrderByDescending(x => x.WorkDate).Select(x => ToDto(x)).ToListAsync());
        }

        [HttpGet("my-summary")]
        public async Task<ActionResult<AttendanceSummaryDto>> GetMySummary([FromQuery] int? month, [FromQuery] int? year)
        {
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized();
            var query = _context.Attendances.AsNoTracking().Where(x => x.EmployeeId == employeeId);
            if (month.HasValue && (month < 1 || month > 12)) return BadRequest("Month không hợp lệ.");
            if (year.HasValue && (year < 2000 || year > 2100)) return BadRequest("Year không hợp lệ.");
            if (month.HasValue) query = query.Where(x => x.WorkDate.Month == month.Value);
            if (year.HasValue) query = query.Where(x => x.WorkDate.Year == year.Value);
            return Ok(await query.GroupBy(x => 1).Select(g => new AttendanceSummaryDto
            {
                TotalDays = g.Count(),
                PresentDays = g.Count(x => x.Status == "PRESENT"),
                TotalWorkedMinutes = g.Sum(x => x.WorkedMinutes),
                TotalLateMinutes = g.Sum(x => x.LateMinutes),
                TotalEarlyLeaveMinutes = g.Sum(x => x.EarlyLeaveMinutes)
            }).FirstOrDefaultAsync() ?? new AttendanceSummaryDto());
        }

        [HttpGet("{id:long}")]
        public async Task<ActionResult<AttendanceItemDto>> GetById(long id)
        {
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized();
            var item = await _context.Attendances.AsNoTracking().Where(x => x.AttendanceId == id && x.EmployeeId == employeeId)
                .Select(x => ToDto(x)).FirstOrDefaultAsync();
            return item == null ? NotFound() : Ok(item);
        }

        private static AttendanceItemDto ToDto(Attendance x) => new()
        {
            Id = x.AttendanceId,
            WorkDate = x.WorkDate,
            CheckInTime = x.CheckInTime,
            CheckOutTime = x.CheckOutTime,
            LateMinutes = x.LateMinutes,
            EarlyLeaveMinutes = x.EarlyLeaveMinutes,
            WorkedMinutes = x.WorkedMinutes,
            OvertimeMinutes = x.OvertimeMinutes,
            Status = x.Status,
            Note = x.Note
        };

        [Authorize(Roles = "Admin,HR,Manager")]
        [HttpGet("admin")]
        [ProducesResponseType(typeof(PagedResponse<AdminAttendanceDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> AdminList([FromQuery] int? branchId, [FromQuery] int? employeeId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!ValidatePage(page, pageSize) || branchId is <= 0 || employeeId is <= 0 || from > to) return BadRequest(new { message = "Bộ lọc hoặc phân trang không hợp lệ." });
            if (!TryResolveAdminBranch(branchId, out var resolvedBranch, out var forbidden)) return forbidden!;
            var query = AdminAttendanceQuery(resolvedBranch, employeeId, from, to, status);
            return Ok(await PageAsync(query, page, pageSize));
        }

        [Authorize(Roles = "Admin,HR,Manager")]
        [HttpGet("admin/employee/{employeeId:int}")]
        [ProducesResponseType(typeof(PagedResponse<AdminAttendanceDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> AdminEmployee(int employeeId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (employeeId <= 0 || !ValidatePage(page, pageSize) || from > to) return BadRequest(new { message = "Bộ lọc không hợp lệ." });
            var employeeBranch = await _context.Employees.Where(x => x.Id == employeeId).Select(x => (int?)x.BranchId).FirstOrDefaultAsync();
            if (!employeeBranch.HasValue) return NotFound();
            if (!TryResolveAdminBranch(employeeBranch, out _, out var forbidden)) return forbidden!;
            return Ok(await PageAsync(AdminAttendanceQuery(null, employeeId, from, to, null), page, pageSize));
        }

        [Authorize(Roles = "Admin,HR,Manager")]
        [HttpGet("admin/date")]
        [ProducesResponseType(typeof(PagedResponse<AdminAttendanceDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> AdminDate([FromQuery] DateTime date, [FromQuery] int? branchId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (date == default || !ValidatePage(page, pageSize) || branchId is <= 0) return BadRequest(new { message = "Ngày hoặc phân trang không hợp lệ." });
            if (!TryResolveAdminBranch(branchId, out var resolvedBranch, out var forbidden)) return forbidden!;
            return Ok(await PageAsync(AdminAttendanceQuery(resolvedBranch, null, date.Date, date.Date, null), page, pageSize));
        }

        [Authorize(Roles = "Admin,HR,Manager")]
        [HttpPut("{id:long}/adjust")]
        public async Task<ActionResult<AttendanceItemDto>> Adjust(long id, [FromBody] AdjustAttendanceRequest request)
        {
            if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Reason)) return BadRequest(new { message = "Thông tin điều chỉnh không hợp lệ." });
            if (!TryGetCurrentEmployeeId(out var adminId)) return Unauthorized();
            var attendance = await _context.Attendances.Include(x => x.Employee).FirstOrDefaultAsync(x => x.AttendanceId == id);
            if (attendance == null) return NotFound();
            if (User.IsInRole("Manager"))
            {
                if (!int.TryParse(User.FindFirstValue("BranchId"), out var managerBranch) || managerBranch != attendance.BranchId) return Forbid();
            }
            if (request.CheckOutTime.HasValue && request.CheckInTime.HasValue && request.CheckOutTime < request.CheckInTime) return BadRequest(new { message = "Check-out không được trước check-in." });
            if (request.CheckOutTime.HasValue && !request.CheckInTime.HasValue && attendance.CheckInTime != default && request.CheckOutTime < attendance.CheckInTime) return BadRequest(new { message = "Check-out không được trước check-in." });
            DateTime? oldIn = attendance.CheckInTime == default ? null : attendance.CheckInTime;
            var oldOut = attendance.CheckOutTime;
            attendance.CheckInTime = request.CheckInTime ?? attendance.CheckInTime;
            attendance.CheckOutTime = request.CheckOutTime;
            attendance.WorkedMinutes = attendance.CheckOutTime.HasValue && attendance.CheckInTime != default ? Math.Max(0, (int)(attendance.CheckOutTime.Value - attendance.CheckInTime).TotalMinutes) : 0;
            var shift = await _context.EmployeeShifts.Where(x => x.EmployeeId == attendance.EmployeeId && x.EffectiveFromDate <= attendance.WorkDate && (!x.EffectiveToDate.HasValue || x.EffectiveToDate >= attendance.WorkDate) && x.Shift!.IsActive).OrderByDescending(x => x.EffectiveFromDate).Select(x => x.Shift).FirstOrDefaultAsync();
            attendance.LateMinutes = shift != null && attendance.CheckInTime != default ? Math.Max(0, (int)(attendance.CheckInTime - attendance.WorkDate.Add(shift.StartTime)).TotalMinutes) : 0;
            attendance.EarlyLeaveMinutes = shift != null && attendance.CheckOutTime.HasValue ? Math.Max(0, (int)(attendance.WorkDate.Add(shift.EndTime) - attendance.CheckOutTime.Value).TotalMinutes) : 0;
            attendance.UpdatedAt = DateTime.Now;
            _context.AttendanceAdjustments.Add(new AttendanceAdjustment { AttendanceId = id, AdminEmployeeId = adminId, PreviousCheckInTime = oldIn, PreviousCheckOutTime = oldOut, NewCheckInTime = attendance.CheckInTime == default ? null : attendance.CheckInTime, NewCheckOutTime = attendance.CheckOutTime, Reason = request.Reason.Trim() });
            await _context.SaveChangesAsync();
            return Ok(ToDto(attendance));
        }

        private IQueryable<AdminAttendanceDto> AdminAttendanceQuery(int? branchId, int? employeeId, DateTime? from, DateTime? to, string? status) => _context.Attendances.AsNoTracking().Where(x => (!branchId.HasValue || x.BranchId == branchId) && (!employeeId.HasValue || x.EmployeeId == employeeId) && (!from.HasValue || x.WorkDate >= from) && (!to.HasValue || x.WorkDate <= to) && (string.IsNullOrWhiteSpace(status) || x.Status == status)).Select(x => new AdminAttendanceDto { AttendanceId = x.AttendanceId, EmployeeId = x.EmployeeId, EmployeeCode = x.Employee!.EmployeeCode, EmployeeName = x.Employee.FullName, BranchId = x.BranchId, BranchName = x.Branch!.Name, WorkDate = x.WorkDate, CheckInTime = x.CheckInTime, CheckOutTime = x.CheckOutTime, LateMinutes = x.LateMinutes, EarlyLeaveMinutes = x.EarlyLeaveMinutes, Status = x.Status, Note = x.Note });

        private async Task<PagedResponse<T>> PageAsync<T>(IQueryable<T> query, int page, int pageSize)
        {
            var total = await query.CountAsync();
            return new PagedResponse<T> { Items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), Page = page, PageSize = pageSize, TotalItems = total, TotalPages = (int)Math.Ceiling(total / (double)pageSize) };
        }

        private static bool ValidatePage(int page, int pageSize) => page >= 1 && pageSize is >= 1 and <= 100;
        private bool TryResolveAdminBranch(int? requested, out int? branchId, out IActionResult? forbidden)
        {
            branchId = requested; forbidden = null;
            if (User.IsInRole("Manager")) { if (!int.TryParse(User.FindFirstValue("BranchId"), out var own) || (requested.HasValue && requested.Value != own)) { forbidden = Forbid(); return false; } branchId = own; }
            return true;
        }

        [HttpPost("register-face")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> RegisterFace([FromBody] RegisterFaceRequest request)
        {
            if (request == null || request.EmployeeId <= 0 || string.IsNullOrWhiteSpace(request.FaceImageBase64))
                return BadRequest(new { message = "Mã nhân viên và ảnh khuôn mặt không được để trống." });
            if (!TryGetCurrentEmployeeId(out var currentEmployeeId))
                return Unauthorized(new { message = "Token không hợp lệ." });
            if (request.EmployeeId != currentEmployeeId && !User.IsInRole("Admin") && !User.IsInRole("HR") && !User.IsInRole("Manager"))
                return Forbid();
            bool result = await _attendanceService.RegisterFaceAsync(request);
            if (!result) return BadRequest(new { message = "Không tìm thấy nhân viên" });
            return Ok(new MessageResponse { Message = "Đăng ký khuôn mặt ONNX gốc thành công!" });
        }

        [HttpPost("check-in")]
        [ProducesResponseType(typeof(AttendanceActionResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> CheckIn([FromBody] AttendanceRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Bssid) || string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.FaceImageBase64))
                return BadRequest(new { message = "Thiếu BSSID, mã thiết bị hoặc ảnh khuôn mặt." });
            if (!TryGetCurrentEmployeeId(out var currentEmployeeId)) return Unauthorized(new { message = "Token không hợp lệ." });
            int employeeId = currentEmployeeId;

            var clientIp = _clientIpResolver.Resolve(HttpContext) ?? string.Empty;

            var result = await _attendanceService.ProcessCheckInAsync(employeeId, clientIp, request);
            if (!result.Success) return BadRequest(result);

            return Ok(new AttendanceActionResponse { Success = result.Success, Message = result.Message, Timestamp = result.Timestamp, Action = result.Action, FaceConfidence = result.FaceConfidence });
        }

        [HttpPost("check-out")]
        [ProducesResponseType(typeof(AttendanceActionResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> CheckOut([FromBody] AttendanceRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Bssid) || string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.FaceImageBase64))
                return BadRequest(new { message = "Thiếu BSSID, mã thiết bị hoặc ảnh khuôn mặt." });
            if (!TryGetCurrentEmployeeId(out var employeeId)) return Unauthorized(new { message = "Token không hợp lệ." });
            var clientIp = _clientIpResolver.Resolve(HttpContext) ?? string.Empty;

            var result = await _attendanceService.ProcessCheckOutAsync(employeeId, clientIp, request);
            if (!result.Success) return BadRequest(result);

            return Ok(new AttendanceActionResponse { Success = result.Success, Message = result.Message, Timestamp = result.Timestamp, Action = result.Action, FaceConfidence = result.FaceConfidence });
        }

        private bool TryGetCurrentEmployeeId(out int employeeId)
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            return int.TryParse(claim?.Value, out employeeId) && employeeId > 0;
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpPost("add-ip-manual")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> AddPublicIpManual([FromBody] AddIpRequest request)
        {
            if (request == null || request.BranchId <= 0)
                return BadRequest(new { message = "Chi nhánh không được để trống." });
            if (!await _context.Branches.AnyAsync(x => x.Id == request.BranchId && x.IsActive))
                return BadRequest(new { message = "Chi nhánh không tồn tại hoặc đã bị khóa." });
            string? resolvedIp;
            if (string.IsNullOrWhiteSpace(request.PublicIp))
                resolvedIp = _clientIpResolver.Resolve(HttpContext);
            else if (!_clientIpResolver.TryNormalizePublic(request.PublicIp, out resolvedIp))
                return BadRequest(new { message = "IP phải là địa chỉ public hợp lệ." });
            if (resolvedIp == null)
                return BadRequest(new { message = "Không xác định được IP public của thiết bị." });
            var newIp = new BranchPublicIp
            {
                BranchId = request.BranchId,
                PublicIp = resolvedIp,
                Description = request.Description ?? "Nhập thủ công từ App Admin"
            };

            _context.BranchPublicIps.Add(newIp);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Thêm IP thủ công thành công!" });
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpPost("register-wifi")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> RegisterWifi([FromBody] RegisterWifiRequest request)
        {
            if (request == null || request.BranchId <= 0 || string.IsNullOrWhiteSpace(request.SSID) || string.IsNullOrWhiteSpace(request.BSSID))
                return BadRequest(new { message = "Chi nhánh, SSID và BSSID không được để trống." });
            if (!await _context.Branches.AnyAsync(x => x.Id == request.BranchId && x.IsActive))
                return BadRequest(new { message = "Chi nhánh không tồn tại hoặc đã bị khóa." });
            string normalizedBssid = request.BSSID.Trim().ToLowerInvariant();

            if (!Regex.IsMatch(normalizedBssid, "^[0-9a-f]{2}([:-][0-9a-f]{2}){5}$"))
                return BadRequest(new { message = "BSSID/Địa chỉ MAC Wi-Fi không hợp lệ!" });

            bool exists = await _context.BranchWifis.AnyAsync(w => w.BranchId == request.BranchId && w.BSSID == normalizedBssid);
            if (exists)
                return BadRequest(new { message = "BSSID Wi-Fi này đã tồn tại trong chi nhánh!" });

            var wifi = new BranchWifi
            {
                BranchId = request.BranchId,
                SSID = request.SSID.Trim(),
                BSSID = normalizedBssid,
                Description = request.IsAutoCaptured ? "Quét tự động từ App" : "Nhập thủ công"
            };

            _context.BranchWifis.Add(wifi);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Lưu thông tin Wi-Fi thành công!", bssid = normalizedBssid });
        }
    }
}