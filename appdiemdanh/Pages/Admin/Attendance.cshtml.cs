using AppDiemDanh.Models;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class AttendanceModel : AdminPageModel
{
    private readonly AppDbContext _context;
    public DateTime? Date { get; private set; }
    public int Month { get; private set; } = DateTime.Today.Month;
    public int Year { get; private set; } = DateTime.Today.Year;
    public int BranchId { get; private set; }
    public List<Branch> Branches { get; private set; } = [];
    public List<Attendance> Records { get; private set; } = [];
    public AttendanceModel(AppDbContext context) => _context = context;
    public async Task<IActionResult> OnGetAsync(DateTime? date, int? branchId, int? month, int? year)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!await HasPermissionAsync("Attendance", "View")) return Forbid();
        var selectedDate = date?.Date;
        var selectedMonth = month is >= 1 and <= 12 ? month.Value : selectedDate?.Month ?? DateTime.Today.Month;
        var selectedYear = year is >= 2000 and <= 2100 ? year.Value : selectedDate?.Year ?? DateTime.Today.Year;
        Date = selectedDate;
        Month = selectedMonth;
        Year = selectedYear;
        Branches = await _context.Branches.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        BranchId = branchId.GetValueOrDefault();
        if (BranchId != 0 && !Branches.Any(x => x.Id == BranchId)) BranchId = 0;

        var query = _context.Attendances
            .Include(x => x.Employee)
            .AsQueryable();
        query = Date.HasValue
            ? query.Where(x => x.WorkDate == Date.Value)
            : query.Where(x => x.WorkDate >= new DateTime(Year, Month, 1) && x.WorkDate < new DateTime(Year, Month, 1).AddMonths(1));
        if (BranchId != 0) query = query.Where(x => x.BranchId == BranchId);
        Records = await query.OrderByDescending(x => x.CheckInTime).ThenByDescending(x => x.AttendanceId).ThenBy(x => x.Employee!.EmployeeCode).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnGetExportExcelAsync(DateTime? date, int? branchId, int? month, int? year)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (!await HasPermissionAsync("Attendance", "View")) return Forbid();

        var selectedDate = date?.Date;
        var selectedMonth = month is >= 1 and <= 12 ? month.Value : selectedDate?.Month ?? DateTime.Today.Month;
        var selectedYear = year is >= 2000 and <= 2100 ? year.Value : selectedDate?.Year ?? DateTime.Today.Year;
        var selectedBranchId = branchId.GetValueOrDefault();

        if (selectedBranchId != 0 && !await _context.Branches.AnyAsync(x => x.Id == selectedBranchId && x.IsActive))
            selectedBranchId = 0;

        var query = _context.Attendances
            .AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Branch)
            .AsQueryable();
        query = selectedDate.HasValue
            ? query.Where(x => x.WorkDate == selectedDate.Value)
            : query.Where(x => x.WorkDate >= new DateTime(selectedYear, selectedMonth, 1) && x.WorkDate < new DateTime(selectedYear, selectedMonth, 1).AddMonths(1));
        if (selectedBranchId != 0) query = query.Where(x => x.BranchId == selectedBranchId);

        var records = await query
            .OrderByDescending(x => x.CheckInTime)
            .ThenByDescending(x => x.AttendanceId)
            .ThenBy(x => x.Employee!.EmployeeCode)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Lịch sử chấm công");
        var headers = new[]
        {
            "STT", "Mã nhân viên", "Họ và tên", "Chi nhánh", "Ngày làm việc",
            "Check-in", "Check-out", "Đi trễ (phút)", "Về sớm (phút)",
            "Đã làm (phút)", "Tăng ca (phút)", "Trạng thái", "Ghi chú"
        };

        for (var column = 0; column < headers.Length; column++)
            worksheet.Cell(1, column + 1).Value = headers[column];

        var header = worksheet.Row(1);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#4F46E5");
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var row = index + 2;
            worksheet.Cell(row, 1).Value = index + 1;
            worksheet.Cell(row, 2).Value = record.Employee?.EmployeeCode ?? string.Empty;
            worksheet.Cell(row, 3).Value = record.Employee?.FullName ?? string.Empty;
            worksheet.Cell(row, 4).Value = record.Branch?.Name ?? string.Empty;
            worksheet.Cell(row, 5).Value = record.WorkDate;
            worksheet.Cell(row, 5).Style.DateFormat.Format = "dd/MM/yyyy";

            if (record.CheckInTime != default)
            {
                worksheet.Cell(row, 6).Value = record.CheckInTime;
                worksheet.Cell(row, 6).Style.DateFormat.Format = "HH:mm";
            }
            if (record.CheckOutTime.HasValue)
            {
                worksheet.Cell(row, 7).Value = record.CheckOutTime.Value;
                worksheet.Cell(row, 7).Style.DateFormat.Format = "HH:mm";
            }

            worksheet.Cell(row, 8).Value = record.LateMinutes;
            worksheet.Cell(row, 9).Value = record.EarlyLeaveMinutes;
            worksheet.Cell(row, 10).Value = record.WorkedMinutes;
            worksheet.Cell(row, 11).Value = record.OvertimeMinutes;
            worksheet.Cell(row, 12).Value = record.Status;
            worksheet.Cell(row, 13).Value = record.Note ?? string.Empty;
        }

        worksheet.RangeUsed()?.SetAutoFilter();
        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns().AdjustToContents();
        worksheet.Column(13).Width = Math.Min(Math.Max(worksheet.Column(13).Width, 20), 50);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var scope = selectedDate.HasValue
            ? selectedDate.Value.ToString("yyyyMMdd")
            : $"{selectedYear}_{selectedMonth:00}";
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"LichSuChamCong_{scope}.xlsx");
    }
}
