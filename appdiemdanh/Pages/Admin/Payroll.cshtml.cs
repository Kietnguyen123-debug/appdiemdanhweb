using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using AppDiemDanh.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class PayrollModel : AdminPageModel
{
    private readonly AppDbContext _context;
    private readonly IPayrollService _service;

    public List<PayslipResponse> Items { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public int Month { get; set; } = DateTime.Today.Month;
    [BindProperty(SupportsGet = true)] public int Year { get; set; } = DateTime.Today.Year;
    [BindProperty(SupportsGet = true)] public int StandardWorkDays { get; set; } = 26;

    public PayrollModel(AppDbContext context, IPayrollService service)
    {
        _context = context;
        _service = service;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        await LoadDataAsync();
        return Page();
    }

    // Handler Xuất Excel hỗ trợ lọc "all" hoặc "bank"
    public async Task<IActionResult> OnGetExportExcelAsync(string filter = "all")
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");

        await LoadDataAsync();

        // Lọc dữ liệu xuất
        var exportItems = Items;
        if (string.Equals(filter, "bank", StringComparison.OrdinalIgnoreCase))
        {
            // Chỉ lấy nhân viên chọn Chuyển khoản (không phân biệt chữ hoa/thường)
            exportItems = Items.Where(x => string.Equals(x.PaymentMethod, "BankTransfer", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (exportItems.Count == 0)
        {
            return Page();
        }

        using var workbook = new XLWorkbook();

        if (string.Equals(filter, "bank", StringComparison.OrdinalIgnoreCase))
        {
            // ==========================================
            // MẪU EXCEL 1: CHỈ CHUYỂN KHOẢN (GỬI NGÂN HÀNG)
            // ==========================================
            var worksheet = workbook.Worksheets.Add($"ChuyenKhoan_{Month}_{Year}");

            worksheet.Cell(1, 1).Value = "STT";
            worksheet.Cell(1, 2).Value = "Mã NV";
            worksheet.Cell(1, 3).Value = "Họ và tên";
            worksheet.Cell(1, 4).Value = "Tên ngân hàng";
            worksheet.Cell(1, 5).Value = "Số tài khoản";
            worksheet.Cell(1, 6).Value = "Tên chủ tài khoản";
            worksheet.Cell(1, 7).Value = "Số tiền thực nhận (VND)";
            worksheet.Cell(1, 8).Value = "Nội dung chuyển khoản";

            var header = worksheet.Row(1);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#059669"); // Màu xanh lá ngân hàng
            header.Style.Font.FontColor = XLColor.White;

            int row = 2;
            int stt = 1;
            foreach (var item in exportItems)
            {
                worksheet.Cell(row, 1).Value = stt++;
                worksheet.Cell(row, 2).Value = item.EmployeeCode;
                worksheet.Cell(row, 3).Value = item.FullName;
                worksheet.Cell(row, 4).Value = item.BankName ?? "Chưa cập nhật";

                var accountCell = worksheet.Cell(row, 5);
                accountCell.SetValue(item.BankAccountNo ?? "");
                accountCell.Style.NumberFormat.Format = "@"; // Dạng chuỗi giữ số 0 đầu

                worksheet.Cell(row, 6).Value = string.IsNullOrWhiteSpace(item.BankAccountHolder) ? item.FullName : item.BankAccountHolder;

                var salaryCell = worksheet.Cell(row, 7);
                salaryCell.Value = item.NetSalary;
                salaryCell.Style.NumberFormat.Format = "#,##0";

                worksheet.Cell(row, 8).Value = $"Chuyen luong thang {Month}/{Year} NV {item.EmployeeCode}";

                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DanhSach_ChuyenKhoan_Thang_{Month}_{Year}.xlsx");
        }
        else
        {
            // ==========================================
            // MẪU EXCEL 2: TOÀN BỘ BẢNG LƯƠNG (ĐỂ DUYỆT)
            // ==========================================
            var worksheet = workbook.Worksheets.Add($"BangLuong_{Month}_{Year}");

            worksheet.Cell(1, 1).Value = "Mã NV";
            worksheet.Cell(1, 2).Value = "Họ và tên";
            worksheet.Cell(1, 3).Value = "Hình thức";
            worksheet.Cell(1, 4).Value = "Ngân hàng";
            worksheet.Cell(1, 5).Value = "Số tài khoản";
            worksheet.Cell(1, 6).Value = "Chủ tài khoản";
            worksheet.Cell(1, 7).Value = "Công";
            worksheet.Cell(1, 8).Value = "Lương công";
            worksheet.Cell(1, 9).Value = "OT";
            worksheet.Cell(1, 10).Value = "Bảo hiểm";
            worksheet.Cell(1, 11).Value = "Thuế TNCN";
            worksheet.Cell(1, 12).Value = "Tạm ứng";
            worksheet.Cell(1, 13).Value = "Thực nhận";

            var header = worksheet.Row(1);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#4F46E5"); // Màu xanh tím quản trị
            header.Style.Font.FontColor = XLColor.White;

            int row = 2;
            foreach (var item in Items)
            {
                worksheet.Cell(row, 1).Value = item.EmployeeCode;
                worksheet.Cell(row, 2).Value = item.FullName;
                worksheet.Cell(row, 3).Value = string.Equals(item.PaymentMethod, "BankTransfer", StringComparison.OrdinalIgnoreCase) ? "Chuyển khoản" : "Tiền mặt";
                worksheet.Cell(row, 4).Value = string.Equals(item.PaymentMethod, "BankTransfer", StringComparison.OrdinalIgnoreCase) ? (item.BankName ?? "—") : "—";

                var accountCell = worksheet.Cell(row, 5);
                accountCell.SetValue(string.Equals(item.PaymentMethod, "BankTransfer", StringComparison.OrdinalIgnoreCase) ? (item.BankAccountNo ?? "—") : "—");
                accountCell.Style.NumberFormat.Format = "@";

                worksheet.Cell(row, 6).Value = string.Equals(item.PaymentMethod, "BankTransfer", StringComparison.OrdinalIgnoreCase) ? (item.BankAccountHolder ?? item.FullName) : "—";
                worksheet.Cell(row, 7).Value = $"{item.ActualWorkDays}/{item.StandardWorkDays}";
                worksheet.Cell(row, 8).Value = item.GrossSalary - item.OvertimeSalary - item.Allowance;
                worksheet.Cell(row, 9).Value = item.OvertimeSalary;
                worksheet.Cell(row, 10).Value = item.InsuranceDeduction;
                worksheet.Cell(row, 11).Value = item.Pit;
                worksheet.Cell(row, 12).Value = item.SalaryAdvanceDeduction;
                worksheet.Cell(row, 13).Value = item.NetSalary;

                worksheet.Range(row, 8, row, 13).Style.NumberFormat.Format = "#,##0";
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"BangLuong_TongHop_Thang_{Month}_{Year}.xlsx");
        }
    }

    private async Task LoadDataAsync()
    {
        Items.Clear();
        var employees = await _context.Employees.Where(x => x.IsActive).OrderByDescending(x => x.Id).ToListAsync();
        foreach (var x in employees)
        {
            Items.Add(await _service.CalculateMonthlyPayslipAsync(x.Id, Month, Year, StandardWorkDays));
        }
    }
}