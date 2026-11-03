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
public class SalaryAdvanceController : ControllerBase
{
    private readonly ISalaryAdvanceService _service;
    private readonly AppDbContext _context;
    public SalaryAdvanceController(ISalaryAdvanceService service, AppDbContext context) { _service = service; _context = context; }

    [Authorize(Roles = "Admin")]
    [HttpGet("admin")]
    [ProducesResponseType(typeof(PagedResponse<AdminSalaryAdvanceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdminList([FromQuery] int? branchId, [FromQuery] int? employeeId, [FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (page < 1 || pageSize is < 1 or > 100 || branchId is <= 0 || employeeId is <= 0 || from > to) return BadRequest();
        if (User.IsInRole("Manager"))
        {
            if (!int.TryParse(User.FindFirstValue("BranchId"), out var ownBranch)) return Forbid();
            if (branchId.HasValue && branchId != ownBranch) return Forbid();
            branchId = ownBranch;
        }
        var query = _context.SalaryAdvances.AsNoTracking().Where(x => (!branchId.HasValue || x.Employee!.BranchId == branchId) && (!employeeId.HasValue || x.EmployeeId == employeeId) && (string.IsNullOrWhiteSpace(status) || x.Status == status) && (!from.HasValue || x.RequestDate >= from) && (!to.HasValue || x.RequestDate <= to)).Select(x => new AdminSalaryAdvanceDto { Id = x.Id, EmployeeId = x.EmployeeId, EmployeeCode = x.Employee!.EmployeeCode, EmployeeName = x.Employee.FullName, BranchId = x.Employee.BranchId, Amount = x.Amount, Reason = x.Reason, RequestDate = x.RequestDate, Status = x.Status, ApprovedById = x.ApprovedById, ReviewNote = x.ReviewNote, CreatedAt = x.CreatedAt });
        var total = await query.CountAsync(); return Ok(new PagedResponse<AdminSalaryAdvanceDto> { Items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), Page = page, PageSize = pageSize, TotalItems = total, TotalPages = (int)Math.Ceiling(total / (double)pageSize) });
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(EmployeeSalaryAdvanceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(long id)
    {
        if (!TryGetEmployeeId(out var employeeId)) return Unauthorized();
        var item = await _context.SalaryAdvances.AsNoTracking().Where(x => x.Id == id && x.EmployeeId == employeeId)
            .Select(x => new EmployeeSalaryAdvanceDto { Id = x.Id, Amount = x.Amount, Reason = x.Reason, RequestDate = x.RequestDate, Status = x.Status, ReviewNote = x.ReviewNote }).FirstOrDefaultAsync();
        return item == null ? NotFound() : Ok(item);
    }

    [HttpPut("{id:long}/cancel")]
    [ProducesResponseType(typeof(CancelRequestResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(long id)
    {
        if (!TryGetEmployeeId(out var employeeId)) return Unauthorized();
        var item = await _context.SalaryAdvances.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
        if (item == null) return NotFound();
        if (item.Status != "Pending") return Conflict(new { message = "Chỉ có thể hủy yêu cầu đang chờ duyệt." });
        item.Status = "Cancelled"; await _context.SaveChangesAsync();
        return Ok(new CancelRequestResponse { Id = id, Status = item.Status });
    }

    [HttpPost("request")]
    [ProducesResponseType(typeof(SalaryAdvanceResponse), StatusCodes.Status200OK)]
    public new async Task<IActionResult> Request([FromBody] SalaryAdvanceRequest request)
    {
        if (request == null || request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "Số tiền và lý do tạm ứng không được để trống hoặc không hợp lệ." });
        if (!TryGetEmployeeId(out var employeeId)) return Unauthorized(new { message = "Token không hợp lệ." });
        var result = await _service.RequestAsync(employeeId, request);
        return result == null ? BadRequest(new { message = "Thông tin tạm ứng không hợp lệ." }) : Ok(ToResponse(result));
    }

    [HttpGet("my-requests")]
    [ProducesResponseType(typeof(IReadOnlyList<SalaryAdvanceResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MyRequests()
    {
        if (!TryGetEmployeeId(out var employeeId)) return Unauthorized(new { message = "Token không hợp lệ." });
        return Ok((await _service.GetMyRequestsAsync(employeeId)).Select(ToResponse).ToList());
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("pending")]
    [ProducesResponseType(typeof(IReadOnlyList<EmployeeSalaryAdvanceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Pending()
    {
        var items = await _context.SalaryAdvances.AsNoTracking().Where(x => x.Status == "Pending").Select(x => new EmployeeSalaryAdvanceDto { Id = x.Id, Amount = x.Amount, Reason = x.Reason, RequestDate = x.RequestDate, Status = x.Status, ReviewNote = x.ReviewNote }).ToListAsync();
        return Ok(items);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:long}/approve")]
    [ProducesResponseType(typeof(SalaryAdvanceResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(long id, [FromBody] SalaryAdvanceReviewRequest request)
    {
        if (id <= 0 || request == null) return BadRequest(new { message = "Thông tin duyệt tạm ứng không hợp lệ." });
        if (!TryGetEmployeeId(out var employeeId)) return Unauthorized(new { message = "Token không hợp lệ." });
        var result = await _service.ReviewAsync(id, employeeId, request);
        return result == null ? NotFound(new { message = "Không tìm thấy đơn hoặc đơn đã được xử lý." }) : Ok(ToResponse(result));
    }

    private static SalaryAdvanceResponse ToResponse(SalaryAdvance item) => new()
    {
        Id = item.Id,
        EmployeeId = item.EmployeeId,
        Amount = item.Amount,
        Reason = item.Reason,
        RequestDate = item.RequestDate,
        Status = item.Status,
        ApprovedById = item.ApprovedById,
        ReviewNote = item.ReviewNote,
        CreatedAt = item.CreatedAt
    };

    private bool TryGetEmployeeId(out int employeeId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out employeeId) && employeeId > 0;
}
