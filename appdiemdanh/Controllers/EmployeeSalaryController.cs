using AppDiemDanh.Models;
using AppDiemDanh.ViewModels;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Controllers;

[Route("EmployeeSalary/[action]")]
public sealed class EmployeeSalaryController : Controller
{
    private readonly AppDbContext _context;

    public EmployeeSalaryController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? branchId, int? departmentId, long? salaryTemplateId, string? search)
    {
        if (!IsAdminSession()) return Unauthorized();
        return Ok(await BuildIndexAsync(branchId, departmentId, salaryTemplateId, search));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkAssign([FromBody] BulkAssignSalaryRequest? request)
    {
        if (!IsAdminSession()) return Unauthorized(new { success = false, message = "Phiên đăng nhập đã hết hạn." });
        if (request is null || !request.EmployeeIds.Any() || request.SalaryTemplateId <= 0)
            return BadRequest(new { success = false, message = "Vui lòng chọn nhân viên và mẫu lương." });

        var effectiveDate = request.EffectiveDate == default ? DateTime.Today : request.EffectiveDate.Date;
        var template = await _context.SalaryTemplates
            .Include(x => x.Details)
            .FirstOrDefaultAsync(x => x.Id == request.SalaryTemplateId && x.IsActive);
        if (template is null)
            return BadRequest(new { success = false, message = "Mẫu lương không tồn tại hoặc đã ngừng sử dụng." });

        var employeeIds = request.EmployeeIds.Distinct().ToList();
        var employees = await _context.Employees.Where(x => employeeIds.Contains(x.Id) && x.IsActive).ToListAsync();
        if (employees.Count != employeeIds.Count)
            return BadRequest(new { success = false, message = "Một hoặc nhiều nhân viên không còn hoạt động." });

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await CloseCurrentSalariesAsync(employeeIds, effectiveDate);
            var newSalaries = employees.Select(employee => new EmployeeSalary
            {
                EmployeeId = employee.Id,
                SalaryTemplateId = template.Id,
                EffectiveDate = effectiveDate,
                Status = "Active",
                CreatedBy = CurrentAdminId()
            }).ToList();
            _context.EmployeeSalaries.AddRange(newSalaries);
            await _context.SaveChangesAsync();

            var templateDetails = template.Details.ToList();
            _context.EmployeeSalaryDetails.AddRange(newSalaries.SelectMany(salary => templateDetails.Select(detail => new EmployeeSalaryDetail
            {
                EmployeeSalaryId = salary.Id,
                SalaryComponentId = detail.SalaryComponentId,
                Amount = detail.DefaultValue,
                Formula = detail.Formula
            })));
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Json(new
            {
                success = true,
                message = $"Đã áp dụng mẫu lương cho {employees.Count} nhân viên.",
                employeeIds,
                templateId = template.Id,
                templateName = template.Name
            });
        }
        catch (DbUpdateException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Không thể lưu gán mẫu lương. Hãy kiểm tra các bảng EmployeeSalaries và EmployeeSalaryDetails." });
        }
        catch (DbException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Database chưa có bảng EmployeeSalaries/EmployeeSalaryDetails. Hãy chạy docs/employee-salary-assignment-schema.sql." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetEmployeeSalaryConfig(int employeeId, long? templateId)
    {
        if (!IsAdminSession()) return Unauthorized();

        var employee = await _context.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId && x.IsActive);
        if (employee is null) return NotFound();

        var current = await CurrentSalaryQuery(employeeId).FirstOrDefaultAsync();
        var selectedTemplateId = templateId ?? current?.SalaryTemplateId;
        var templates = await _context.SalaryTemplates.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new SalaryTemplateFilterViewModel { Id = x.Id, Name = x.Name }).ToListAsync();
        var currentDetails = current is null
            ? []
            : await _context.EmployeeSalaryDetails.AsNoTracking().Where(x => x.EmployeeSalaryId == current.Id)
                .ToDictionaryAsync(x => x.SalaryComponentId);

        var components = selectedTemplateId.HasValue
            ? await _context.SalaryTemplateDetails.AsNoTracking().Where(x => x.SalaryTemplateId == selectedTemplateId.Value)
                .OrderBy(x => x.SalaryComponent!.Name)
                .Select(x => new EmployeeSalaryComponentViewModel
                {
                    ComponentId = x.SalaryComponentId,
                    Code = x.SalaryComponent!.Code,
                    Name = x.SalaryComponent.Name,
                    Amount = currentDetails.ContainsKey(x.SalaryComponentId) ? currentDetails[x.SalaryComponentId].Amount : x.DefaultValue,
                    Formula = currentDetails.ContainsKey(x.SalaryComponentId) ? currentDetails[x.SalaryComponentId].Formula : x.Formula
                }).ToListAsync()
            : [];

        return PartialView("~/Views/EmployeeSalary/_ConfigDrawer.cshtml", new EmployeeSalaryDrawerViewModel
        {
            EmployeeId = employee.Id,
            EmployeeCode = employee.EmployeeCode,
            FullName = employee.FullName,
            SalaryId = current?.Id,
            SalaryTemplateId = selectedTemplateId,
            IsCasualIncome = current?.IsCasualIncome ?? false,
            Templates = templates,
            Components = components
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveEmployeeSalary([FromBody] SaveEmployeeSalaryRequest? request)
    {
        if (!IsAdminSession()) return Unauthorized(new { success = false, message = "Phiên đăng nhập đã hết hạn." });
        if (request is null || request.EmployeeId <= 0 || request.SalaryTemplateId <= 0)
            return BadRequest(new { success = false, message = "Dữ liệu cấu hình chưa đầy đủ." });

        var employeeExists = await _context.Employees.AnyAsync(x => x.Id == request.EmployeeId && x.IsActive);
        var template = await _context.SalaryTemplates.Include(x => x.Details)
            .FirstOrDefaultAsync(x => x.Id == request.SalaryTemplateId && x.IsActive);
        if (!employeeExists || template is null)
            return NotFound(new { success = false, message = "Nhân viên hoặc mẫu lương không tồn tại." });

        var allowedComponentIds = template.Details.Select(x => x.SalaryComponentId).ToHashSet();
        var inputs = request.Components.Where(x => allowedComponentIds.Contains(x.ComponentId)).GroupBy(x => x.ComponentId).Select(x => x.First()).ToList();
        var effectiveDate = request.EffectiveDate == default ? DateTime.Today : request.EffectiveDate.Date;

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await CloseCurrentSalariesAsync([request.EmployeeId], effectiveDate);
            var salary = new EmployeeSalary
            {
                EmployeeId = request.EmployeeId,
                SalaryTemplateId = template.Id,
                EffectiveDate = effectiveDate,
                IsCasualIncome = request.IsCasualIncome,
                Status = "Active",
                CreatedBy = CurrentAdminId()
            };
            _context.EmployeeSalaries.Add(salary);
            await _context.SaveChangesAsync();
            _context.EmployeeSalaryDetails.AddRange(inputs.Select(input => new EmployeeSalaryDetail
            {
                EmployeeSalaryId = salary.Id,
                SalaryComponentId = input.ComponentId,
                Amount = input.Amount,
                Formula = string.IsNullOrWhiteSpace(input.Formula) ? null : input.Formula.Trim()
            }));
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Json(new
            {
                success = true,
                message = "Đã lưu cấu hình lương cá nhân.",
                employeeId = request.EmployeeId,
                templateId = template.Id,
                templateName = template.Name,
                isCasualIncome = request.IsCasualIncome
            });
        }
        catch (DbUpdateException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Không thể lưu cấu hình lương. Hãy kiểm tra các bảng EmployeeSalaries và EmployeeSalaryDetails." });
        }
        catch (DbException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Database chưa có bảng EmployeeSalaries/EmployeeSalaryDetails. Hãy chạy docs/employee-salary-assignment-schema.sql." });
        }
    }

    private async Task<EmployeeSalaryIndexViewModel> BuildIndexAsync(int? branchId, int? departmentId, long? salaryTemplateId, string? search)
    {
        var today = DateTime.Today;
        var currentSalaries = _context.EmployeeSalaries.AsNoTracking()
            .Where(x => x.Status == "Active" && x.EffectiveDate <= today && (x.EndDate == null || x.EndDate >= today));
        var query = _context.Employees.AsNoTracking().Where(x => x.IsActive);
        if (branchId.HasValue) query = query.Where(x => x.BranchId == branchId.Value);
        if (departmentId.HasValue) query = query.Where(x => x.DepartmentId == departmentId.Value);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.EmployeeCode.Contains(search) || x.FullName.Contains(search));
        if (salaryTemplateId == 0) query = query.Where(x => !currentSalaries.Any(s => s.EmployeeId == x.Id));
        else if (salaryTemplateId.HasValue) query = query.Where(x => currentSalaries.Any(s => s.EmployeeId == x.Id && s.SalaryTemplateId == salaryTemplateId.Value));

        var employees = await query.OrderBy(x => x.EmployeeCode).Select(x => new EmployeeSalaryRowViewModel
        {
            Id = x.Id,
            EmployeeCode = x.EmployeeCode,
            FullName = x.FullName,
            DepartmentName = x.Department == null ? "Chưa phân phòng ban" : x.Department.Name,
            BranchName = x.Branch!.Name,
            IsActive = x.IsActive,
            SalaryId = currentSalaries.Where(s => s.EmployeeId == x.Id).OrderByDescending(s => s.EffectiveDate).Select(s => (long?)s.Id).FirstOrDefault(),
            SalaryTemplateId = currentSalaries.Where(s => s.EmployeeId == x.Id).OrderByDescending(s => s.EffectiveDate).Select(s => s.SalaryTemplateId).FirstOrDefault(),
            SalaryTemplateName = currentSalaries.Where(s => s.EmployeeId == x.Id).OrderByDescending(s => s.EffectiveDate).Select(s => s.SalaryTemplate!.Name).FirstOrDefault()
        }).ToListAsync();

        return new EmployeeSalaryIndexViewModel
        {
            Employees = employees,
            Departments = await _context.Departments.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new DepartmentFilterViewModel { Id = x.Id, Name = x.Name }).ToListAsync(),
            Templates = await _context.SalaryTemplates.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new SalaryTemplateFilterViewModel { Id = x.Id, Name = x.Name }).ToListAsync()
        };
    }

    private IQueryable<EmployeeSalary> CurrentSalaryQuery(int employeeId) => _context.EmployeeSalaries
        .Where(x => x.EmployeeId == employeeId && x.Status == "Active" && x.EffectiveDate <= DateTime.Today && (x.EndDate == null || x.EndDate >= DateTime.Today))
        .OrderByDescending(x => x.EffectiveDate);

    private async Task CloseCurrentSalariesAsync(IEnumerable<int> employeeIds, DateTime effectiveDate)
    {
        var current = await _context.EmployeeSalaries.Where(x => employeeIds.Contains(x.EmployeeId) && x.Status == "Active" && x.EffectiveDate <= effectiveDate).ToListAsync();
        foreach (var salary in current)
        {
            salary.Status = "Inactive";
            salary.EndDate = effectiveDate.AddDays(-1);
        }
    }

    private bool IsAdminSession() => HttpContext.Session.GetInt32("AdminEmployeeId").HasValue
        && HttpContext.Session.GetString("AdminRole") is "Admin" or "HR";

    private int CurrentAdminId() => HttpContext.Session.GetInt32("AdminEmployeeId") ?? 0;
}
