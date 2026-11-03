using AppDiemDanh.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using AppDiemDanh.DTOs;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Mvc;

namespace AppDiemDanh.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class BranchController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IClientIpResolver _clientIpResolver;

    public BranchController(AppDbContext context, IClientIpResolver clientIpResolver)
    {
        _context = context;
        _clientIpResolver = clientIpResolver;
    }

    [HttpGet]
    public async Task<ActionResult<BranchListResponse>> GetAll()
    {
        var branches = await _context.Branches
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new BranchListItemResponse
            {
                Id = x.Id,
                Name = x.Name
            })
            .ToListAsync();

        return Ok(new BranchListResponse { Success = true, Data = branches });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<BranchResponse>> GetById(int id)
    {
        if (id <= 0) return BadRequest();
        var branch = await _context.Branches.AsNoTracking().Where(x => x.Id == id).Select(x => new BranchResponse { Id = x.Id, Name = x.Name, IsActive = x.IsActive, EmployeeCount = _context.Employees.Count(e => e.BranchId == x.Id), PublicIpCount = x.PublicIps.Count, WifiCount = x.Wifis.Count }).FirstOrDefaultAsync();
        return branch == null ? NotFound() : Ok(branch);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<BranchResponse>> Create([FromBody] BranchRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { message = "Tên chi nhánh không được để trống." });
        var name = request.Name.Trim();
        if (await _context.Branches.AnyAsync(x => x.Name == name)) return Conflict(new { message = "Tên chi nhánh đã tồn tại." });
        var branch = new Branch { Name = name, IsActive = true }; _context.Branches.Add(branch); await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = branch.Id }, new BranchResponse { Id = branch.Id, Name = branch.Name, IsActive = branch.IsActive });
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<BranchResponse>> Update(int id, [FromBody] BranchRequest request)
    {
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { message = "Thông tin chi nhánh không hợp lệ." });
        var branch = await _context.Branches.FindAsync(id); if (branch == null) return NotFound();
        var name = request.Name.Trim(); if (await _context.Branches.AnyAsync(x => x.Id != id && x.Name == name)) return Conflict(new { message = "Tên chi nhánh đã tồn tại." });
        branch.Name = name; await _context.SaveChangesAsync(); return Ok(new BranchResponse { Id = branch.Id, Name = branch.Name, IsActive = branch.IsActive });
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}/toggle-status")]
    public async Task<ActionResult<BranchStatusResponse>> ToggleStatus(int id)
    {
        var branch = await _context.Branches.FindAsync(id); if (branch == null) return NotFound(); branch.IsActive = !branch.IsActive; await _context.SaveChangesAsync(); return Ok(new BranchStatusResponse { Id = branch.Id, IsActive = branch.IsActive });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id)
    {
        var branch = await _context.Branches.FindAsync(id); if (branch == null) return NotFound();
        if (await _context.Employees.AnyAsync(x => x.BranchId == id) || await _context.Attendances.AnyAsync(x => x.BranchId == id) || await _context.BranchPublicIps.AnyAsync(x => x.BranchId == id) || await _context.BranchWifis.AnyAsync(x => x.BranchId == id) || await _context.LeaveRequests.AnyAsync(x => x.Employee!.BranchId == id) || await _context.OvertimeRecords.AnyAsync(x => x.Employee!.BranchId == id) || await _context.SalaryAdvances.AnyAsync(x => x.Employee!.BranchId == id) || await _context.EmployeeShifts.AnyAsync(x => x.Employee!.BranchId == id)) return Conflict(new { message = "Không thể xóa chi nhánh đang có dữ liệu liên quan; hãy vô hiệu hóa chi nhánh." });
        _context.Branches.Remove(branch); await _context.SaveChangesAsync(); return NoContent();
    }

    // ✅ Cho phép mọi User đã đăng nhập xem IP chi nhánh
    [Authorize]
    [HttpGet("{branchId:int}/ips")]
    [ProducesResponseType(typeof(IReadOnlyList<BranchIpDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIps(int branchId) => await BranchExists(branchId) ? Ok(await _context.BranchPublicIps.AsNoTracking().Where(x => x.BranchId == branchId && x.IsActive).Select(x => new BranchIpDto { Id = x.Id, BranchId = x.BranchId, PublicIp = x.PublicIp, Description = x.Description, IsActive = x.IsActive }).ToListAsync()) : NotFound();

    [Authorize(Roles = "Admin")]
    [HttpPut("{branchId:int}/ips/{id:int}")]
    public async Task<ActionResult<BranchIpDto>> UpdateIp(int branchId, int id, [FromBody] BranchIpRequest request)
    {
        if (!_clientIpResolver.TryNormalizePublic(request?.PublicIp, out var normalizedIp)) return BadRequest(new { message = "IP phải là địa chỉ public hợp lệ." });
        var item = await _context.BranchPublicIps.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == branchId); if (item == null) return NotFound();
        var value = normalizedIp!; if (await _context.BranchPublicIps.AnyAsync(x => x.Id != id && x.BranchId == branchId && x.PublicIp == value)) return Conflict(new { message = "IP đã tồn tại trong chi nhánh." });
        item.PublicIp = value; item.Description = request.Description?.Trim() ?? string.Empty; await _context.SaveChangesAsync(); return Ok(new BranchIpDto { Id = item.Id, BranchId = item.BranchId, PublicIp = item.PublicIp, Description = item.Description, IsActive = item.IsActive });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{branchId:int}/ips/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteIp(int branchId, int id) { var item = await _context.BranchPublicIps.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == branchId); if (item == null) return NotFound(); _context.BranchPublicIps.Remove(item); await _context.SaveChangesAsync(); return NoContent(); }

    [Authorize(Roles = "Admin")]
    [HttpPut("{branchId:int}/ips/{id:int}/toggle-status")]
    [ProducesResponseType(typeof(BranchStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ToggleIp(int branchId, int id) { var item = await _context.BranchPublicIps.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == branchId); if (item == null) return NotFound(); item.IsActive = !item.IsActive; await _context.SaveChangesAsync(); return Ok(new BranchStatusResponse { Id = item.Id, IsActive = item.IsActive }); }

    [Authorize]
    [HttpGet("{branchId:int}/wifis")]
    [ProducesResponseType(typeof(IReadOnlyList<BranchWifiDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWifis(int branchId)
    {
        if (!await BranchExists(branchId)) return NotFound();

        var wifis = await _context.BranchWifis
            .AsNoTracking()
            .Where(x => x.BranchId == branchId && x.IsActive)
            .Select(x => new BranchWifiDto
            {
                Id = x.Id,
                BranchId = x.BranchId,
                Ssid = x.SSID,
                Bssid = x.BSSID,
                Description = x.Description,
                IsActive = x.IsActive
            })
            .ToListAsync();

        return Ok(wifis);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{branchId:int}/wifis/{id:int}")]
    [ProducesResponseType(typeof(BranchWifiDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateWifi(int branchId, int id, [FromBody] BranchWifiRequest request)
    {
        var rawBssid = request?.Bssid?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(request?.Ssid) || rawBssid == null)
            return BadRequest(new { message = "SSID hoặc BSSID không được để trống." });

        // Cho phép Regex linh hoạt cả dạng "5c:18:dd:13:8b:0a" lẫn "5c18dd138b0a"
        bool isValidBssid = Regex.IsMatch(rawBssid, "^[0-9a-f]{2}([:-][0-9a-f]{2}){5}$") || Regex.IsMatch(rawBssid, "^[0-9a-f]{12}$");
        if (!isValidBssid) return BadRequest(new { message = "BSSID không đúng định dạng MAC Address." });

        var item = await _context.BranchWifis.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == branchId);
        if (item == null) return NotFound();

        if (await _context.BranchWifis.AnyAsync(x => x.Id != id && x.BranchId == branchId && x.BSSID == rawBssid))
            return Conflict(new { message = "BSSID đã tồn tại trong chi nhánh." });

        item.SSID = request.Ssid.Trim();
        item.BSSID = rawBssid;
        item.Description = request.Description?.Trim() ?? string.Empty;

        await _context.SaveChangesAsync();
        return Ok(new BranchWifiDto { Id = item.Id, BranchId = item.BranchId, Ssid = item.SSID, Bssid = item.BSSID, Description = item.Description, IsActive = item.IsActive });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{branchId:int}/wifis/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteWifi(int branchId, int id) { var item = await _context.BranchWifis.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == branchId); if (item == null) return NotFound(); _context.BranchWifis.Remove(item); await _context.SaveChangesAsync(); return NoContent(); }

    [Authorize(Roles = "Admin")]
    [HttpPut("{branchId:int}/wifis/{id:int}/toggle-status")]
    [ProducesResponseType(typeof(BranchWifiStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ToggleWifi(int branchId, int id) { var item = await _context.BranchWifis.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == branchId); if (item == null) return NotFound(); item.IsActive = !item.IsActive; await _context.SaveChangesAsync(); return Ok(new BranchWifiStatusResponse { Id = item.Id, IsActive = item.IsActive }); }

    private Task<bool> BranchExists(int id) => _context.Branches.AnyAsync(x => x.Id == id);
}