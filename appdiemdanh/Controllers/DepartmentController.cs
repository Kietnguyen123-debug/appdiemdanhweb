using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DepartmentController : ControllerBase
{
    private readonly AppDbContext _context;

    public DepartmentController(AppDbContext context) => _context = context;

    [Authorize]
    [HttpGet]
    public async Task<ActionResult<DepartmentListResponse>> GetAll()
    {
        var departments = await _context.Departments
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new DepartmentListItemResponse { Id = x.Id, Name = x.Name })
            .ToListAsync();

        return Ok(new DepartmentListResponse { Success = true, Data = departments });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<DepartmentResponse>> GetById(int id)
    {
        if (id <= 0) return BadRequest();
        var department = await ProjectDepartment().FirstOrDefaultAsync(x => x.Id == id);
        return department == null ? NotFound() : Ok(department);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<DepartmentResponse>> Create([FromBody] DepartmentRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Tên phòng ban không được để trống." });

        var name = request.Name.Trim();
        if (await _context.Departments.AnyAsync(x => x.Name == name))
            return Conflict(new { message = "Tên phòng ban đã tồn tại." });

        var department = new Department { Name = name, IsActive = true };
        _context.Departments.Add(department);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = department.Id }, new DepartmentResponse { Id = department.Id, Name = department.Name, IsActive = department.IsActive });
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<DepartmentResponse>> Update(int id, [FromBody] DepartmentRequest request)
    {
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Thông tin phòng ban không hợp lệ." });

        var department = await _context.Departments.FindAsync(id);
        if (department == null) return NotFound();
        var name = request.Name.Trim();
        if (await _context.Departments.AnyAsync(x => x.Id != id && x.Name == name))
            return Conflict(new { message = "Tên phòng ban đã tồn tại." });

        department.Name = name;
        await _context.SaveChangesAsync();
        return Ok(new DepartmentResponse { Id = department.Id, Name = department.Name, IsActive = department.IsActive, EmployeeCount = await _context.Employees.CountAsync(x => x.DepartmentId == department.Id) });
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}/toggle-status")]
    public async Task<ActionResult<DepartmentResponse>> ToggleStatus(int id)
    {
        var department = await _context.Departments.FindAsync(id);
        if (department == null) return NotFound();
        department.IsActive = !department.IsActive;
        await _context.SaveChangesAsync();
        return Ok(new DepartmentResponse { Id = department.Id, Name = department.Name, IsActive = department.IsActive, EmployeeCount = await _context.Employees.CountAsync(x => x.DepartmentId == department.Id) });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id)
    {
        var department = await _context.Departments.FindAsync(id);
        if (department == null) return NotFound();
        _context.Departments.Remove(department);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<DepartmentResponse> ProjectDepartment() => _context.Departments
        .AsNoTracking()
        .Select(x => new DepartmentResponse
        {
            Id = x.Id,
            Name = x.Name,
            IsActive = x.IsActive,
            EmployeeCount = x.Employees.Count()
        });
}