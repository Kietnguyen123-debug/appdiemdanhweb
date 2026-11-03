using AppDiemDanh.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public sealed class ShiftGroupsModel : AdminPageModel
{
    private readonly AppDbContext _db;

    public ShiftGroupsModel(AppDbContext db) => _db = db;

    public List<ShiftGroupRow> Groups { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        return RedirectToPage("/Admin/Shifts/Management", new { view = "shift-groups" });
    }

    public async Task<IActionResult> OnGetModalAsync(int? id)
    {
        if (!IsAdminSession) return Unauthorized();
        var group = id.HasValue
            ? await _db.ShiftGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value)
            : null;
        if (id.HasValue && group == null) return NotFound();

        var selectedEmployeeIds = id.HasValue
            ? await _db.ShiftGroupEmployees.AsNoTracking().Where(x => x.ShiftGroupId == id.Value).Select(x => x.EmployeeId).ToListAsync()
            : [];
        var employees = await _db.Employees.AsNoTracking()
            .Include(x => x.Department)
            .Include(x => x.Branch)
            .Where(x => x.IsActive)
            .OrderBy(x => x.EmployeeCode)
            .Select(x => new ShiftGroupEmployeeOption
            {
                Id = x.Id,
                EmployeeCode = x.EmployeeCode,
                FullName = x.FullName,
                DepartmentName = x.Department == null ? "Chưa phân phòng ban" : x.Department.Name,
                BranchName = x.Branch == null ? "Chưa xác định" : x.Branch.Name,
                IsChecked = selectedEmployeeIds.Contains(x.Id),
                CurrentGroups = _db.ShiftGroupEmployees
                    .Where(m => m.EmployeeId == x.Id && (!id.HasValue || m.ShiftGroupId != id.Value))
                    .Select(m => m.ShiftGroup!.Name)
                    .ToList()
            }).ToListAsync();

        return Partial("_ShiftGroupModal", new ShiftGroupModalViewModel
        {
            Id = group?.Id,
            Code = group?.Code ?? string.Empty,
            Name = group?.Name ?? string.Empty,
            Description = group?.Description,
            Employees = employees
        });
    }

    public async Task<IActionResult> OnGetListAsync()
    {
        if (!IsAdminSession) return Unauthorized();
        await LoadGroupsAsync();
        return Partial("_ShiftGroupList", Groups);
    }

    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OnPostSaveGroupAsync([FromBody] SaveShiftGroupRequest request)
    {
        if (!IsAdminSession) return Unauthorized();
        request.Code = request.Code?.Trim() ?? string.Empty;
        request.Name = request.Name?.Trim() ?? string.Empty;
        request.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return new JsonResult(new { success = false, message = "Mã nhóm và tên nhóm là bắt buộc." }) { StatusCode = 400 };
        if (request.Code.Length > 80 || request.Name.Length > 200 || request.Description?.Length > 1000)
            return new JsonResult(new { success = false, message = "Thông tin nhóm vượt quá độ dài cho phép." }) { StatusCode = 400 };
        if (await _db.ShiftGroups.AnyAsync(x => x.Code == request.Code && x.Id != request.Id))
            return new JsonResult(new { success = false, message = "Mã nhóm đã tồn tại." }) { StatusCode = 409 };

        var employeeIds = (request.EmployeeIds ?? []).Where(x => x > 0).Distinct().ToList();
        var validEmployeeIds = await _db.Employees.Where(x => x.IsActive && employeeIds.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            ShiftGroup group;
            if (request.Id.HasValue)
            {
                group = await _db.ShiftGroups.FirstOrDefaultAsync(x => x.Id == request.Id.Value) ?? throw new InvalidOperationException("Không tìm thấy nhóm phân ca.");
                group.Code = request.Code;
                group.Name = request.Name;
                group.Description = request.Description;
            }
            else
            {
                group = new ShiftGroup { Code = request.Code, Name = request.Name, Description = request.Description };
                _db.ShiftGroups.Add(group);
                await _db.SaveChangesAsync();
            }

            var oldMembers = await _db.ShiftGroupEmployees.Where(x => x.ShiftGroupId == group.Id).ToListAsync();
            _db.ShiftGroupEmployees.RemoveRange(oldMembers);
            _db.ShiftGroupEmployees.AddRange(validEmployeeIds.Select(employeeId => new ShiftGroupEmployee { ShiftGroupId = group.Id, EmployeeId = employeeId }));
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return new JsonResult(new { success = true, message = request.Id.HasValue ? "Đã cập nhật nhóm phân ca." : "Đã tạo nhóm phân ca." });
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync();
            return new JsonResult(new { success = false, message = ex.Message }) { StatusCode = 404 };
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return new JsonResult(new { success = false, message = "Không thể lưu nhóm. Vui lòng kiểm tra mã nhóm." }) { StatusCode = 409 };
        }
    }

    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OnPostDeleteGroupAsync(int id)
    {
        if (!IsAdminSession) return Unauthorized();
        var group = await _db.ShiftGroups.FindAsync(id);
        if (group == null) return new JsonResult(new { success = false, message = "Nhóm phân ca không tồn tại." }) { StatusCode = 404 };
        _db.ShiftGroups.Remove(group);
        await _db.SaveChangesAsync();
        return new JsonResult(new { success = true, message = "Đã xóa nhóm phân ca." });
    }

    private async Task LoadGroupsAsync() => Groups = await _db.ShiftGroups.AsNoTracking()
        .Where(x => x.IsActive)
        .OrderBy(x => x.Name)
        .Select(x => new ShiftGroupRow { Id = x.Id, Code = x.Code, Name = x.Name, Description = x.Description, IsActive = x.IsActive, EmployeeCount = x.Employees.Count })
        .ToListAsync();

    public sealed class ShiftGroupRow
    {
        public int Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public bool IsActive { get; init; }
        public int EmployeeCount { get; init; }
    }

    public sealed class ShiftGroupModalViewModel
    {
        public int? Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public List<ShiftGroupEmployeeOption> Employees { get; init; } = [];
    }

    public sealed class ShiftGroupEmployeeOption
    {
        public int Id { get; init; }
        public string EmployeeCode { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string DepartmentName { get; init; } = string.Empty;
        public string BranchName { get; init; } = string.Empty;
        public bool IsChecked { get; init; }
        public List<string> CurrentGroups { get; init; } = [];
    }

    public sealed class SaveShiftGroupRequest
    {
        public int? Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<int>? EmployeeIds { get; set; }
    }
}
