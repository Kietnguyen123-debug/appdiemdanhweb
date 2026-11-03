using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using AppDiemDanh.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin.Shifts;

public sealed class ManagementModel : AdminPageModel
{
    private readonly AppDbContext _db;
    private readonly IHybridScheduleService _resolver;

    public ManagementModel(AppDbContext db, IHybridScheduleService resolver)
    {
        _db = db;
        _resolver = resolver;
    }

    public DateTime Month { get; private set; }
    public string ViewMode { get; private set; } = "employees";
    public RosterResponse Roster { get; private set; } = new();
    public List<WorkGroup> Groups { get; private set; } = [];
    public List<Shift> Shifts { get; private set; } = [];
    public List<GroupSchedule> GroupSchedules { get; private set; } = [];
    public List<ShiftGroupsModel.ShiftGroupRow> ShiftGroups { get; private set; } = [];
    public string? Message { get; private set; }
    public bool Success { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? year, int? month, string? view)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        await LoadAsync(year, month, view);
        return Page();
    }

    public async Task<IActionResult> OnPostOverrideAsync(int employeeId, DateTime workDate, int overrideShiftId, string reason, int? year, int? month)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (employeeId <= 0 || overrideShiftId <= 0 || string.IsNullOrWhiteSpace(reason))
            return await Reload(year, month, "employees", "Ca và lý do bắt buộc.", false);

        var shiftExists = await _db.Shifts.AnyAsync(x => x.Id == overrideShiftId && x.IsActive);
        if (!shiftExists || !await _db.Employees.AnyAsync(x => x.Id == employeeId && x.IsActive))
            return await Reload(year, month, "employees", "Nhân viên hoặc ca không hợp lệ.", false);

        var item = await _db.EmployeeShiftExceptions.FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.WorkDate == workDate.Date);
        if (item == null)
            _db.EmployeeShiftExceptions.Add(new EmployeeShiftException { EmployeeId = employeeId, WorkDate = workDate.Date, OverrideShiftId = overrideShiftId, Reason = reason.Trim(), CreatedBy = CurrentEmployeeId });
        else
        {
            item.OverrideShiftId = overrideShiftId;
            item.Reason = reason.Trim();
            item.CreatedBy = CurrentEmployeeId;
            item.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return RedirectToPage(new { year = workDate.Year, month = workDate.Month, view = "employees" });
    }

    // KHÔI PHỤC VỀ LỊCH NHÓM CHUNG (Xóa cả ngoại lệ ngày và dọn dẹp khoảng ca cá nhân)
    public async Task<IActionResult> OnPostRevertAsync(long? exceptionId, int? employeeId, DateTime? workDate, int? year, int? month)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");

        int targetEmpId = employeeId ?? 0;
        DateTime? targetDate = workDate?.Date;

        if (exceptionId.HasValue && exceptionId.Value > 0 && (targetEmpId <= 0 || !targetDate.HasValue))
        {
            var exc = await _db.EmployeeShiftExceptions.FindAsync(exceptionId.Value)
                ?? await _db.EmployeeShiftExceptions.FirstOrDefaultAsync(x => x.Id == exceptionId.Value);

            if (exc != null)
            {
                targetEmpId = exc.EmployeeId;
                targetDate = exc.WorkDate.Date;
            }
        }

        if (targetEmpId > 0 && targetDate.HasValue)
        {
            await RemoveEmployeePersonalSchedulesForDateAsync(targetEmpId, targetDate.Value);
            await _db.SaveChangesAsync();
        }

        var resYear = year ?? targetDate?.Year ?? (Month == default ? DateTime.Today.Year : Month.Year);
        var resMonth = month ?? targetDate?.Month ?? (Month == default ? DateTime.Today.Month : Month.Month);

        return RedirectToPage(new { year = resYear, month = resMonth, view = "employees" });
    }

    // TÁCH VÀ XÓA LỊCH CÁ NHÂN TRONG 1 NGÀY BẤM KHÔI PHỤC (XỬ LÝ ĐÚNG CA VÔ THỜI HẠN)
    private async Task RemoveEmployeePersonalSchedulesForDateAsync(int employeeId, DateTime targetDate)
    {
        // 1. Xóa ngoại lệ ngày trong EmployeeShiftExceptions
        var exceptions = await _db.EmployeeShiftExceptions
            .Where(x => x.EmployeeId == employeeId && x.WorkDate == targetDate)
            .ToListAsync();
        _db.EmployeeShiftExceptions.RemoveRange(exceptions);

        // 2. Tìm các khoảng ca cá nhân bị trùng ngày chọn trong EmployeeShifts
        var overlappingShifts = await _db.EmployeeShifts
            .Where(x => x.EmployeeId == employeeId
                && x.EffectiveFromDate <= targetDate
                && (!x.EffectiveToDate.HasValue || x.EffectiveToDate.Value >= targetDate))
            .ToListAsync();

        foreach (var shift in overlappingShifts)
        {
            var shiftFrom = shift.EffectiveFromDate.Date;
            var shiftTo = shift.EffectiveToDate?.Date;

            _db.EmployeeShifts.Remove(shift);

            // Giữ lại khoảng lịch trước ngày targetDate (nếu có)
            if (shiftFrom < targetDate)
            {
                _db.EmployeeShifts.Add(new EmployeeShift
                {
                    EmployeeId = shift.EmployeeId,
                    ShiftId = shift.ShiftId,
                    EffectiveFromDate = shiftFrom,
                    EffectiveToDate = targetDate.AddDays(-1)
                });
            }

            // GIỮ LẠI ĐOẠN CA SAU NGÀY CHỌN (KỂ CẢ KHI CA VÔ THỜI HẠN - NULL)
            if (!shiftTo.HasValue || shiftTo.Value > targetDate)
            {
                _db.EmployeeShifts.Add(new EmployeeShift
                {
                    EmployeeId = shift.EmployeeId,
                    ShiftId = shift.ShiftId,
                    EffectiveFromDate = targetDate.AddDays(1),
                    EffectiveToDate = shiftTo
                });
            }
        }
    }

    public async Task<IActionResult> OnPostRevertGroupScheduleAsync(int workGroupId, DateTime workDate, int? year, int? month)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");

        var item = await _db.GroupSchedules.FirstOrDefaultAsync(x => x.WorkGroupId == workGroupId && x.WorkDate == workDate.Date);
        if (item != null)
        {
            _db.GroupSchedules.Remove(item);
            await _db.SaveChangesAsync();
        }

        var resYear = year ?? workDate.Year;
        var resMonth = month ?? workDate.Month;

        return RedirectToPage(new { year = resYear, month = resMonth, view = "groups" });
    }

    public async Task<IActionResult> OnPostGroupAssignAsync(int workGroupId, int shiftId, DateTime from, DateTime to, int? year, int? month)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        var groupExists = await _db.WorkGroups.AnyAsync(x => x.Id == workGroupId && x.IsActive);
        if (!groupExists || !await _db.Shifts.AnyAsync(x => x.Id == shiftId && x.IsActive) || from.Date > to.Date)
            return await Reload(year, month, "groups", "Nhóm, ca hoặc khoảng ngày không hợp lệ.", false);

        var rangeFrom = from.Date;
        var rangeTo = to.Date;
        var dates = Enumerable.Range(0, (rangeTo - rangeFrom).Days + 1).Select(i => rangeFrom.AddDays(i)).ToArray();

        await using var transaction = await _db.Database.BeginTransactionAsync();
        var schedules = await _db.GroupSchedules.Where(x => x.WorkGroupId == workGroupId && dates.Contains(x.WorkDate)).ToListAsync();

        foreach (var date in dates)
        {
            var item = schedules.FirstOrDefault(x => x.WorkDate == date);
            if (item == null)
                _db.GroupSchedules.Add(new GroupSchedule { WorkGroupId = workGroupId, WorkDate = date, ShiftId = shiftId, CreatedBy = CurrentEmployeeId });
            else
            {
                item.ShiftId = shiftId;
                item.CreatedBy = CurrentEmployeeId;
                item.CreatedAt = DateTime.UtcNow;
            }
        }

        var workGroup = await _db.WorkGroups.AsNoTracking().FirstAsync(x => x.Id == workGroupId);
        var employeeIds = await _db.GroupMembers.AsNoTracking()
            .Where(x => x.WorkGroupId == workGroupId && x.EffectiveDate <= rangeTo && (x.EndDate == null || x.EndDate >= rangeFrom))
            .Select(x => x.EmployeeId)
            .ToListAsync();

        var shiftGroupId = await _db.ShiftGroups.AsNoTracking().Where(x => x.Code == workGroup.Code && x.IsActive).Select(x => (int?)x.Id).FirstOrDefaultAsync();
        if (shiftGroupId.HasValue)
        {
            employeeIds.AddRange(await _db.ShiftGroupEmployees.AsNoTracking().Where(x => x.ShiftGroupId == shiftGroupId.Value).Select(x => x.EmployeeId).ToListAsync());
        }

        await OverwriteEmployeeShiftsAsync(employeeIds.Distinct(), rangeFrom, rangeTo);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return RedirectToPage(new { year = rangeFrom.Year, month = rangeFrom.Month, view = "groups" });
    }

    // GHI ĐÈ / TÁCH KHOẢNG LỊCH CÁ NHÂN KHI GÁN CA NHÓM
    private async Task OverwriteEmployeeShiftsAsync(IEnumerable<int> employeeIds, DateTime from, DateTime to)
    {
        var ids = employeeIds.Distinct().ToArray();
        if (ids.Length == 0) return;

        var overlapping = await _db.EmployeeShifts.Where(x => ids.Contains(x.EmployeeId)
            && x.EffectiveFromDate <= to
            && (!x.EffectiveToDate.HasValue || x.EffectiveToDate.Value >= from)).ToListAsync();

        foreach (var existing in overlapping)
        {
            var existingFrom = existing.EffectiveFromDate.Date;
            var existingTo = existing.EffectiveToDate?.Date;

            var hasLeftPart = existingFrom < from;
            // XỬ LÝ ĐÚNG TRƯỜNG HỢP CA VÔ THỜI HẠN (NULL)
            var hasRightPart = !existingTo.HasValue || existingTo.Value > to;

            _db.EmployeeShifts.Remove(existing);

            if (hasLeftPart)
            {
                _db.EmployeeShifts.Add(new EmployeeShift
                {
                    EmployeeId = existing.EmployeeId,
                    ShiftId = existing.ShiftId,
                    EffectiveFromDate = existingFrom,
                    EffectiveToDate = from.AddDays(-1)
                });
            }

            if (hasRightPart)
            {
                _db.EmployeeShifts.Add(new EmployeeShift
                {
                    EmployeeId = existing.EmployeeId,
                    ShiftId = existing.ShiftId,
                    EffectiveFromDate = to.AddDays(1),
                    EffectiveToDate = existingTo
                });
            }
        }
    }

    public async Task<IActionResult> OnGetShiftGroupModalAsync(int? id)
    {
        if (!IsAdminSession) return Unauthorized();
        var group = id.HasValue ? await _db.ShiftGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value) : null;
        if (id.HasValue && group == null) return NotFound();

        var selectedEmployeeIds = id.HasValue
            ? await _db.ShiftGroupEmployees.AsNoTracking().Where(x => x.ShiftGroupId == id.Value).Select(x => x.EmployeeId).ToListAsync()
            : [];

        var employees = await _db.Employees.AsNoTracking().Include(x => x.Department).Include(x => x.Branch).Where(x => x.IsActive).OrderBy(x => x.EmployeeCode)
            .Select(x => new ShiftGroupsModel.ShiftGroupEmployeeOption
            {
                Id = x.Id,
                EmployeeCode = x.EmployeeCode,
                FullName = x.FullName,
                DepartmentName = x.Department == null ? "Chưa phân phòng ban" : x.Department.Name,
                BranchName = x.Branch == null ? "Chưa xác định" : x.Branch.Name,
                IsChecked = selectedEmployeeIds.Contains(x.Id),
                CurrentGroups = _db.ShiftGroupEmployees.Where(m => m.EmployeeId == x.Id && (!id.HasValue || m.ShiftGroupId != id.Value)).Select(m => m.ShiftGroup!.Name).ToList()
            }).ToListAsync();

        return Partial("~/Pages/Admin/_ShiftGroupModal.cshtml", new ShiftGroupsModel.ShiftGroupModalViewModel
        {
            Id = group?.Id,
            Code = group?.Code ?? string.Empty,
            Name = group?.Name ?? string.Empty,
            Description = group?.Description,
            Employees = employees
        });
    }

    public async Task<IActionResult> OnGetShiftGroupListAsync()
    {
        if (!IsAdminSession) return Unauthorized();
        await LoadShiftGroupsAsync();
        return Partial("~/Pages/Admin/_ShiftGroupList.cshtml", ShiftGroups);
    }

    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OnPostSaveShiftGroupAsync([FromBody] ShiftGroupsModel.SaveShiftGroupRequest request)
    {
        if (!IsAdminSession) return Unauthorized();
        request.Code = request.Code?.Trim() ?? string.Empty;
        request.Name = request.Name?.Trim() ?? string.Empty;
        request.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { success = false, message = "Mã nhóm và tên nhóm là bắt buộc." });

        if (request.Code.Length > 80 || request.Name.Length > 200 || request.Description?.Length > 1000)
            return BadRequest(new { success = false, message = "Thông tin nhóm vượt quá độ dài cho phép." });

        if (await _db.ShiftGroups.AnyAsync(x => x.Code == request.Code && x.Id != request.Id))
            return new JsonResult(new { success = false, message = "Mã nhóm đã tồn tại." }) { StatusCode = 409 };

        var employeeIds = (request.EmployeeIds ?? []).Where(x => x > 0).Distinct().ToList();
        var validEmployeeIds = await _db.Employees.Where(x => x.IsActive && employeeIds.Contains(x.Id)).Select(x => x.Id).ToListAsync();

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            ShiftGroup group;
            var previousCode = request.Id.HasValue
                ? await _db.ShiftGroups.Where(x => x.Id == request.Id.Value).Select(x => x.Code).FirstOrDefaultAsync()
                : null;

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

            await SyncWorkGroupAsync(previousCode ?? group.Code, group, validEmployeeIds);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return new JsonResult(new { success = true, message = request.Id.HasValue ? "Đã cập nhật nhóm phân ca." : "Đã tạo nhóm phân ca." });
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync();
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return new JsonResult(new { success = false, message = "Không thể lưu nhóm. Vui lòng kiểm tra mã nhóm." }) { StatusCode = 409 };
        }
    }

    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OnPostDeleteShiftGroupAsync(int id)
    {
        if (!IsAdminSession) return Unauthorized();
        var group = await _db.ShiftGroups.FindAsync(id);
        if (group == null) return NotFound(new { success = false, message = "Nhóm phân ca không tồn tại." });

        await using var transaction = await _db.Database.BeginTransactionAsync();
        var workGroup = await _db.WorkGroups.FirstOrDefaultAsync(x => x.Code == group.Code);
        if (workGroup != null)
        {
            workGroup.IsActive = false;
            var today = DateTime.UtcNow.Date;
            var memberships = await _db.GroupMembers.Where(x => x.WorkGroupId == workGroup.Id && (x.EndDate == null || x.EndDate >= today)).ToListAsync();
            foreach (var membership in memberships) membership.EndDate = today.AddDays(-1);
        }

        _db.ShiftGroups.Remove(group);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return new JsonResult(new { success = true, message = "Đã xóa nhóm phân ca." });
    }

    private async Task SyncWorkGroupAsync(string previousCode, ShiftGroup shiftGroup, IReadOnlyCollection<int> employeeIds)
    {
        var workGroup = await _db.WorkGroups.FirstOrDefaultAsync(x => x.Code == previousCode)
            ?? await _db.WorkGroups.FirstOrDefaultAsync(x => x.Code == shiftGroup.Code);

        if (workGroup == null)
        {
            workGroup = new WorkGroup { Code = shiftGroup.Code, Name = shiftGroup.Name, IsActive = shiftGroup.IsActive };
            _db.WorkGroups.Add(workGroup);
            await _db.SaveChangesAsync();
        }
        else
        {
            workGroup.Code = shiftGroup.Code;
            workGroup.Name = shiftGroup.Name;
            workGroup.IsActive = shiftGroup.IsActive;
        }

        var today = DateTime.UtcNow.Date;
        var currentMembers = await _db.GroupMembers.Where(x => x.WorkGroupId == workGroup.Id && (x.EndDate == null || x.EndDate >= today)).ToListAsync();
        var selectedIds = employeeIds.ToHashSet();

        foreach (var member in currentMembers.Where(x => !selectedIds.Contains(x.EmployeeId)))
            member.EndDate = today.AddDays(-1);

        var currentIds = currentMembers.Select(x => x.EmployeeId).ToHashSet();
        _db.GroupMembers.AddRange(selectedIds.Where(employeeId => !currentIds.Contains(employeeId))
            .Select(employeeId => new GroupMember { WorkGroupId = workGroup.Id, EmployeeId = employeeId, EffectiveDate = today }));
    }

    private async Task ReconcileShiftGroupsAsync()
    {
        var groups = await _db.ShiftGroups.AsNoTracking().Where(x => x.IsActive).ToListAsync();
        foreach (var group in groups)
        {
            var employeeIds = await _db.ShiftGroupEmployees.AsNoTracking()
                .Where(x => x.ShiftGroupId == group.Id)
                .Select(x => x.EmployeeId)
                .ToListAsync();
            await SyncWorkGroupAsync(group.Code, group, employeeIds);
        }
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
    }

    private async Task<IActionResult> Reload(int? year, int? month, string view, string message, bool success)
    {
        await LoadAsync(year, month, view);
        Message = message;
        Success = success;
        return Page();
    }

    private async Task LoadAsync(int? year, int? month, string? view)
    {
        var y = year is >= 2000 and <= 2100 ? year.Value : DateTime.Today.Year;
        var m = month is >= 1 and <= 12 ? month.Value : DateTime.Today.Month;
        Month = new DateTime(y, m, 1);
        ViewMode = view is "groups" or "shift-groups" ? view : "employees";

        await ReconcileShiftGroupsAsync();
        Groups = await _db.WorkGroups.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        if (ViewMode == "shift-groups") await LoadShiftGroupsAsync();

        Shifts = await _db.Shifts.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.StartTime).ToListAsync();
        GroupSchedules = await _db.GroupSchedules.AsNoTracking().Include(x => x.Shift).Where(x => x.WorkDate >= Month && x.WorkDate < Month.AddMonths(1)).ToListAsync();
        Roster = await _resolver.ResolveAsync(new RosterQuery { From = Month, To = Month.AddMonths(1).AddDays(-1) });
    }

    private async Task LoadShiftGroupsAsync() => ShiftGroups = await _db.ShiftGroups.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
        .Select(x => new ShiftGroupsModel.ShiftGroupRow { Id = x.Id, Code = x.Code, Name = x.Name, Description = x.Description, IsActive = x.IsActive, EmployeeCount = x.Employees.Count }).ToListAsync();
}