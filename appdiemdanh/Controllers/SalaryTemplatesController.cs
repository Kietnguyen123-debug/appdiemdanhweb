using AppDiemDanh.Models;
using AppDiemDanh.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Controllers;

[Route("[controller]/[action]")]
public sealed class SalaryTemplatesController : Controller
{
    private readonly AppDbContext _context;

    public SalaryTemplatesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!IsAdminSession()) return RedirectToPage("/Admin/Login");

        var templates = await _context.SalaryTemplates
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new SalaryTemplateListItemViewModel
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                PositionGroup = x.PositionGroup,
                ComponentCount = x.Details.Count
            })
            .ToListAsync();

        return View(new SalaryTemplateIndexViewModel { Templates = templates });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateSalaryTemplateViewModel model)
    {
        if (!IsAdminSession()) return RedirectToPage("/Admin/Login");
        if (!ModelState.IsValid) return View("Index", await BuildIndexViewModelAsync());

        var code = model.Code.Trim();
        if (await _context.SalaryTemplates.AnyAsync(x => x.Code == code))
        {
            ModelState.AddModelError(nameof(model.Code), "Mã mẫu lương đã tồn tại.");
            return View("Index", await BuildIndexViewModelAsync());
        }

        var template = new SalaryTemplate
        {
            Code = code,
            Name = model.Name.Trim(),
            PositionGroup = string.IsNullOrWhiteSpace(model.PositionGroup) ? null : model.PositionGroup.Trim()
        };

        _context.SalaryTemplates.Add(template);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Detail), new { id = template.Id });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detail(long id)
    {
        if (!IsAdminSession()) return RedirectToPage("/Admin/Login");

        var template = await _context.SalaryTemplates
            .AsNoTracking()
            .Include(x => x.Details)
            .FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (template is null) return NotFound();

        var selected = template.Details.ToDictionary(x => x.SalaryComponentId);
        var components = await _context.SalaryComponents
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .ToListAsync();

        var rows = components.Select(component =>
        {
            selected.TryGetValue(component.Id, out var detail);
            return new SalaryTemplateComponentRowViewModel
            {
                ComponentId = component.Id,
                Code = component.Code,
                Name = component.Name,
                Category = component.Category,
                CalculationType = component.CalculationType,
                Selected = detail is not null,
                DefaultValue = detail?.DefaultValue,
                Formula = detail?.Formula
            };
        }).ToList();

        return View(new SalaryTemplateDetailViewModel
        {
            Id = template.Id,
            Code = template.Code,
            Name = template.Name,
            PositionGroup = template.PositionGroup,
            Components = rows
        });
    }

    [HttpPost("{id:long}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveComponents(long id, [FromBody] SaveSalaryTemplateComponentsRequest? request)
    {
        if (!IsAdminSession()) return Unauthorized(new { success = false, message = "Phiên đăng nhập đã hết hạn." });
        if (request is null) return BadRequest(new { success = false, message = "Dữ liệu cấu hình không hợp lệ." });

        var template = await _context.SalaryTemplates.FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (template is null) return NotFound(new { success = false, message = "Không tìm thấy mẫu lương." });

        var inputs = request.Components ?? [];
        var selectedInputs = inputs.Where(x => x.Selected).ToList();
        if (selectedInputs.GroupBy(x => x.ComponentId).Any(x => x.Count() > 1))
            return BadRequest(new { success = false, message = "Danh sách thành phần bị trùng." });

        var componentIds = selectedInputs.Select(x => x.ComponentId).ToHashSet();
        var activeComponents = await _context.SalaryComponents
            .Where(x => x.IsActive && componentIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync();
        if (activeComponents.Count != componentIds.Count)
            return BadRequest(new { success = false, message = "Một hoặc nhiều thành phần lương không còn hoạt động." });

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var existing = await _context.SalaryTemplateDetails.Where(x => x.SalaryTemplateId == id).ToListAsync();
        _context.SalaryTemplateDetails.RemoveRange(existing);
        _context.SalaryTemplateDetails.AddRange(selectedInputs.Select(input => new SalaryTemplateDetail
        {
            SalaryTemplateId = id,
            SalaryComponentId = input.ComponentId,
            DefaultValue = input.DefaultValue,
            Formula = string.IsNullOrWhiteSpace(input.Formula) ? null : input.Formula.Trim()
        }));
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return Json(new { success = true, message = "Đã lưu cấu hình mẫu lương." });
    }

    private async Task<SalaryTemplateIndexViewModel> BuildIndexViewModelAsync()
    {
        return new SalaryTemplateIndexViewModel
        {
            Templates = await _context.SalaryTemplates
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new SalaryTemplateListItemViewModel
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    PositionGroup = x.PositionGroup,
                    ComponentCount = x.Details.Count
                })
                .ToListAsync()
        };
    }

    private bool IsAdminSession() => HttpContext.Session.GetInt32("AdminEmployeeId").HasValue
        && HttpContext.Session.GetString("AdminRole") is "Admin" or "HR";
}
