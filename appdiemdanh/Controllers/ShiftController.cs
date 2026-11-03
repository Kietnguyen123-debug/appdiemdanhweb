using AppDiemDanh.DTOs;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AppDiemDanh.Models;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Controllers
{
[Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ShiftController : ControllerBase
    {
        private readonly IShiftService _shiftService;
        private readonly AppDbContext _context;
        private readonly IHybridScheduleService _resolver;

        public ShiftController(IShiftService shiftService, AppDbContext context, IHybridScheduleService resolver)
        {
            _shiftService = shiftService;
            _context = context;
            _resolver = resolver;
        }

        [HttpGet("my-today")]
        [ProducesResponseType(typeof(EmployeeShiftScheduleDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> MyToday()
        {
            if (!TryGetEmployeeId(out var employeeId)) return Unauthorized();
            var date = DateTime.Today;
            var roster = await _resolver.ResolveAsync(new RosterQuery { From = date, To = date });
            var day = roster.Employees.FirstOrDefault(x => x.EmployeeId == employeeId)?.Days.FirstOrDefault();
            return day?.ShiftId == null ? NotFound() : Ok(ToScheduleDto(day));
        }

        [HttpGet("my-schedule")]
        [ProducesResponseType(typeof(IReadOnlyList<EmployeeShiftScheduleDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> MySchedule([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (!TryGetEmployeeId(out var employeeId)) return Unauthorized();
            var start = (from ?? DateTime.Today).Date;
            var end = (to ?? start).Date;
            if (start > end || (end - start).TotalDays > 366) return BadRequest(new { message = "Khoảng ngày không hợp lệ." });
            var roster = await _resolver.ResolveAsync(new RosterQuery { From = start, To = end });
            var days = roster.Employees.FirstOrDefault(x => x.EmployeeId == employeeId)?.Days ?? [];
            var schedules = new List<EmployeeShiftScheduleDto>();
            foreach (var day in days.Where(x => x.ShiftId.HasValue))
            {
                var current = schedules.LastOrDefault();
                if (current != null && current.Shift.Id == day.ShiftId && current.EffectiveToDate?.Date.AddDays(1) == day.WorkDate.Date)
                {
                    current.EffectiveToDate = day.WorkDate.Date;
                    continue;
                }
                schedules.Add(ToScheduleDto(day));
            }
            return Ok(schedules);
        }

        private static EmployeeShiftScheduleDto ToScheduleDto(ResolvedShiftDto day) => new()
        {
            AssignmentId = day.AssignmentId ?? 0,
            EffectiveFromDate = day.WorkDate.Date,
            EffectiveToDate = day.WorkDate.Date,
            Shift = new ShiftDto
            {
                Id = day.ShiftId!.Value,
                ShiftCode = day.ShiftCode ?? string.Empty,
                ShiftName = day.ShiftName ?? string.Empty,
                StartTime = day.StartTime ?? TimeSpan.Zero,
                EndTime = day.EndTime ?? TimeSpan.Zero,
                IsActive = true
            }
        };

        private bool TryGetEmployeeId(out int employeeId) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out employeeId) && employeeId > 0;

        [Authorize(Roles = "Admin,HR,Manager")]
        [HttpGet("assignments")]
        [ProducesResponseType(typeof(PagedResponse<AdminShiftAssignmentDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAssignments([FromQuery] int? branchId, [FromQuery] int? employeeId, [FromQuery] int? shiftId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (page < 1 || pageSize is < 1 or > 100 || branchId is <= 0 || employeeId is <= 0 || shiftId is <= 0 || from > to) return BadRequest();
            if (User.IsInRole("Manager"))
            {
                if (!int.TryParse(User.FindFirstValue("BranchId"), out var ownBranch)) return Forbid();
                if (branchId.HasValue && branchId != ownBranch) return Forbid();
                branchId = ownBranch;
            }
            var query = _context.EmployeeShifts.AsNoTracking().Where(x => (!branchId.HasValue || x.Employee!.BranchId == branchId) && (!employeeId.HasValue || x.EmployeeId == employeeId) && (!shiftId.HasValue || x.ShiftId == shiftId) && (!from.HasValue || !x.EffectiveToDate.HasValue || x.EffectiveToDate >= from) && (!to.HasValue || x.EffectiveFromDate <= to)).Select(x => new AdminShiftAssignmentDto { Id = x.Id, EmployeeId = x.EmployeeId, EmployeeCode = x.Employee!.EmployeeCode, EmployeeName = x.Employee.FullName, BranchId = x.Employee.BranchId, ShiftId = x.ShiftId, ShiftCode = x.Shift!.ShiftCode, ShiftName = x.Shift.ShiftName, EffectiveFromDate = x.EffectiveFromDate, EffectiveToDate = x.EffectiveToDate });
            var total = await query.CountAsync(); return Ok(new PagedResponse<AdminShiftAssignmentDto> { Items = await query.OrderBy(x => x.EffectiveFromDate).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), Page = page, PageSize = pageSize, TotalItems = total, TotalPages = (int)Math.Ceiling(total / (double)pageSize) });
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpPut("assignments/{id:long}")]
        [ProducesResponseType(typeof(ShiftAssignmentResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateAssignment(long id, [FromBody] UpdateShiftAssignmentRequest request)
        {
            if (id <= 0 || request == null || request.ShiftId <= 0 || request.EffectiveFromDate == default || request.EffectiveToDate < request.EffectiveFromDate) return BadRequest();
            var item = await _context.EmployeeShifts.FirstOrDefaultAsync(x => x.Id == id); if (item == null) return NotFound();
            if (await _context.EmployeeShifts.AnyAsync(x => x.Id != id && x.EmployeeId == item.EmployeeId && x.EffectiveFromDate <= (request.EffectiveToDate ?? DateTime.MaxValue) && (!x.EffectiveToDate.HasValue || x.EffectiveToDate >= request.EffectiveFromDate))) return Conflict(new { message = "Khoảng phân ca bị chồng lấn." });
            if (!await _context.Shifts.AnyAsync(x => x.Id == request.ShiftId && x.IsActive)) return BadRequest(new { message = "Ca không tồn tại hoặc đã khóa." });
            item.ShiftId = request.ShiftId; item.EffectiveFromDate = request.EffectiveFromDate.Date; item.EffectiveToDate = request.EffectiveToDate?.Date; await _context.SaveChangesAsync(); return Ok(new ShiftAssignmentResponse { Id = item.Id, ShiftId = item.ShiftId, EffectiveFromDate = item.EffectiveFromDate, EffectiveToDate = item.EffectiveToDate });
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpDelete("assignments/{id:long}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> DeleteAssignment(long id)
        {
            var item = await _context.EmployeeShifts.FirstOrDefaultAsync(x => x.Id == id); if (item == null) return NotFound();
            if (item.EffectiveFromDate.Date <= DateTime.Today) return Conflict(new { message = "Không thể xóa phân ca đã có hiệu lực; hãy đóng hiệu lực bằng ngày kết thúc." });
            _context.EmployeeShifts.Remove(item); await _context.SaveChangesAsync(); return NoContent();
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ShiftDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll()
        {
            return Ok(await _shiftService.GetAllAsync());
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetById(int id)
        {
            if (id <= 0) return BadRequest(new { message = "Mã ca không hợp lệ." });
            var shift = await _shiftService.GetByIdAsync(id);
            return shift == null ? NotFound() : Ok(shift);
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpPost]
        [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status201Created)]
        public async Task<IActionResult> Create([FromBody] CreateShiftRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ShiftCode) || string.IsNullOrWhiteSpace(request.ShiftName) || request.StartTime < TimeSpan.Zero || request.StartTime >= TimeSpan.FromDays(1) || request.EndTime < TimeSpan.Zero || request.EndTime >= TimeSpan.FromDays(1) || request.StartTime == request.EndTime)
                return BadRequest(new { message = "Mã ca, tên ca và thời gian ca không hợp lệ." });
            var shift = await _shiftService.CreateAsync(request);
            if (shift == null)
                return BadRequest(new { message = "Mã ca đã tồn tại hoặc thông tin ca không hợp lệ." });

            return CreatedAtAction(nameof(GetById), new { id = shift.Id }, shift);
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Update(int id, [FromBody] CreateShiftRequest request)
        {
            if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.ShiftCode) || string.IsNullOrWhiteSpace(request.ShiftName) || request.StartTime < TimeSpan.Zero || request.StartTime >= TimeSpan.FromDays(1) || request.EndTime < TimeSpan.Zero || request.EndTime >= TimeSpan.FromDays(1) || request.StartTime == request.EndTime)
                return BadRequest(new { message = "Mã ca, tên ca hoặc ID không hợp lệ." });
            var shift = await _shiftService.UpdateAsync(id, request);
            if (shift == null)
                return BadRequest(new { message = "Ca không tồn tại, mã ca bị trùng hoặc thông tin không hợp lệ." });

            return Ok(shift);
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Delete(int id)
        {
            if (id <= 0) return BadRequest(new { message = "Mã ca không hợp lệ." });
            try
            {
                if (!await _shiftService.DeleteAsync(id))
                    return NotFound();
            }
            catch (InvalidOperationException)
            {
                return Conflict(new { message = "Không thể xóa ca đang được phân công cho nhân viên." });
            }

            return NoContent();
        }

        [Authorize(Roles = "Admin,HR")]
        [HttpPost("assignments")]
        [ProducesResponseType(typeof(IdMessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Assign([FromBody] AssignShiftRequest request)
        {
            if (request == null || request.EmployeeId <= 0 || request.ShiftId <= 0 || request.EffectiveFromDate == default || (request.EffectiveToDate.HasValue && request.EffectiveToDate < request.EffectiveFromDate))
                return BadRequest(new { message = "Thông tin phân ca hoặc khoảng ngày không hợp lệ." });
            var assignmentId = await _shiftService.AssignShiftToEmployeeAsync(request);
            if (!assignmentId.HasValue)
                return BadRequest(new { message = "Nhân viên/ca không hợp lệ, khoảng ngày không hợp lệ hoặc bị chồng lấn." });

            return Ok(new IdMessageResponse { Id = assignmentId.Value, Message = "Gán ca cho nhân viên thành công." });
        }
    }
}
