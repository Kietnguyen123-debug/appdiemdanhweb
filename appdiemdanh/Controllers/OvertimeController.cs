using System.Security.Claims;
using AppDiemDanh.DTOs;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class OvertimeController : ControllerBase
{
    private readonly IOvertimeService _service;
    private readonly AppDbContext _context;
    public OvertimeController(IOvertimeService service, AppDbContext context) { _service = service; _context = context; }

    [Authorize(Roles = "Admin,HR,Manager")]
    [HttpGet("admin")]
        [ProducesResponseType(typeof(PagedResponse<AdminOvertimeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdminList([FromQuery] int? branchId, [FromQuery] int? employeeId, [FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (page < 1 || pageSize is < 1 or > 100 || branchId is <= 0 || employeeId is <= 0 || from > to) return BadRequest();
        if (User.IsInRole("Manager"))
        {
            if (!int.TryParse(User.FindFirstValue("BranchId"), out var ownBranch)) return Forbid();
            if (branchId.HasValue && branchId != ownBranch) return Forbid();
            branchId = ownBranch;
        }
        var query = _context.OvertimeRecords.AsNoTracking().Where(x => (!branchId.HasValue || x.Employee!.BranchId == branchId) && (!employeeId.HasValue || x.EmployeeId == employeeId) && (string.IsNullOrWhiteSpace(status) || x.Status == status) && (!from.HasValue || x.WorkDate >= from) && (!to.HasValue || x.WorkDate <= to)).Select(x => new AdminOvertimeDto { Id = x.Id, EmployeeId = x.EmployeeId, EmployeeCode = x.Employee!.EmployeeCode, EmployeeName = x.Employee.FullName, BranchId = x.Employee.BranchId, WorkDate = x.WorkDate, Hours = x.Hours, Multiplier = x.Multiplier, Status = x.Status });
        return Ok(await PageAsync(query, page, pageSize));
    }

    [Authorize(Roles = "Admin,HR,Manager")]
    [HttpGet("admin/pending")]
        [ProducesResponseType(typeof(PagedResponse<AdminOvertimeDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> AdminPending([FromQuery] int? branchId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => AdminList(branchId, null, "Pending", null, null, page, pageSize);

    private async Task<PagedResponse<T>> PageAsync<T>(IQueryable<T> query, int page, int pageSize) { var total = await query.CountAsync(); return new PagedResponse<T> { Items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), Page = page, PageSize = pageSize, TotalItems = total, TotalPages = (int)Math.Ceiling(total / (double)pageSize) }; }

    [HttpGet("my-requests")]
        [ProducesResponseType(typeof(IReadOnlyList<EmployeeOvertimeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MyRequests()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)) return Unauthorized();
        return Ok(await _context.OvertimeRecords.AsNoTracking().Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.WorkDate)
            .Select(x => new EmployeeOvertimeDto { Id = x.Id, WorkDate = x.WorkDate, Hours = x.Hours, Multiplier = x.Multiplier, Status = x.Status }).ToListAsync());
    }

    [HttpGet("{id:long}")]
        [ProducesResponseType(typeof(EmployeeOvertimeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(long id)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)) return Unauthorized();
        var item = await _context.OvertimeRecords.AsNoTracking().Where(x => x.Id == id && x.EmployeeId == employeeId)
            .Select(x => new EmployeeOvertimeDto { Id = x.Id, WorkDate = x.WorkDate, Hours = x.Hours, Multiplier = x.Multiplier, Status = x.Status }).FirstOrDefaultAsync();
        return item == null ? NotFound() : Ok(item);
    }

    [HttpPut("{id:long}/cancel")]
        [ProducesResponseType(typeof(CancelRequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(long id)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)) return Unauthorized();
        var item = await _context.OvertimeRecords.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
        if (item == null) return NotFound();
        if (item.Status != "Pending") return Conflict(new { message = "Chỉ có thể hủy yêu cầu đang chờ duyệt." });
        item.Status = "Cancelled"; await _context.SaveChangesAsync();
        return Ok(new CancelRequestResponse { Id = id, Status = item.Status });
    }

    [HttpPost("log")]
        [ProducesResponseType(typeof(OvertimeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Log([FromBody] OvertimeRequest request)
    {
        if (request == null || request.WorkDate == default || !double.IsFinite(request.Hours) || request.Hours <= 0 || request.Hours > 24 || !double.IsFinite(request.Multiplier) || request.Multiplier <= 0 || request.Multiplier > 10)
            return BadRequest(new { message = "Ngày, số giờ hoặc hệ số làm thêm không hợp lệ." });
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId) || employeeId <= 0)
            return Unauthorized(new { message = "Token không hợp lệ." });
        var result = await _service.LogAsync(employeeId, request);
        return result == null ? BadRequest(new { message = "Thông tin làm thêm không hợp lệ." }) : Ok(new OvertimeResponse { Id = result.Id, EmployeeId = result.EmployeeId, WorkDate = result.WorkDate, Hours = result.Hours, Multiplier = result.Multiplier, Status = result.Status });
    }

    [Authorize(Roles = "Admin,Manager")]
    [HttpPut("{id:long}/approve")]
        [ProducesResponseType(typeof(OvertimeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(long id, [FromBody] OvertimeReviewRequest request)
    {
        if (id <= 0 || request == null) return BadRequest(new { message = "Thông tin duyệt OT không hợp lệ." });
        if (User.IsInRole("Manager"))
        {
            if (!int.TryParse(User.FindFirstValue("BranchId"), out var managerBranch))
                return Forbid();
            var requestBranch = await _context.OvertimeRecords
                .Where(x => x.Id == id)
                .Select(x => (int?)x.Employee!.BranchId)
                .FirstOrDefaultAsync();
            if (!requestBranch.HasValue) return NotFound();
            if (requestBranch.Value != managerBranch) return Forbid();
        }
        var result = await _service.ReviewAsync(id, request);
        return result == null ? NotFound(new { message = "Không tìm thấy bản ghi hoặc đã được xử lý." }) : Ok(new OvertimeResponse { Id = result.Id, EmployeeId = result.EmployeeId, WorkDate = result.WorkDate, Hours = result.Hours, Multiplier = result.Multiplier, Status = result.Status });
    }

}
