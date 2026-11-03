using AppDiemDanh.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class PaymentConfigModel : AdminPageModel
{
    private readonly AppDbContext _context;

    public List<Branch> Branches { get; private set; } = [];
    public List<EmployeePaymentItem> Employees { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public string? SearchTerm { get; set; }

    [BindProperty] public PaymentUpdateInput Input { get; set; } = new();

    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public PaymentConfigModel(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");

        if (Input.EmployeeId <= 0)
            return SetError("Vui lòng chọn nhân viên hợp lệ.");

        if (Input.PaymentMethod == "BankTransfer" && string.IsNullOrWhiteSpace(Input.BankAccountNo))
            return SetError("Vui lòng nhập Số tài khoản khi chọn hình thức Chuyển khoản.");

        try
        {
            var emp = await _context.Employees.FirstOrDefaultAsync(x => x.Id == Input.EmployeeId);
            if (emp == null) return SetError("Không tìm thấy nhân viên.");

            // Lấy cấu hình lương đang hoạt động
            var currentConfig = await _context.EmployeeSalaryConfigs
                .Where(x => x.EmployeeId == Input.EmployeeId && x.IsActive)
                .OrderByDescending(x => x.EffectiveFrom)
                .FirstOrDefaultAsync();

            if (currentConfig != null)
            {
                // Cập nhật thông tin ngân hàng vào cấu hình hiện tại
                currentConfig.PaymentMethod = Input.PaymentMethod;
                currentConfig.BankName = Input.PaymentMethod == "Cash" ? null : Input.BankName?.Trim();
                currentConfig.BankAccountNo = Input.PaymentMethod == "Cash" ? null : Input.BankAccountNo?.Trim();
                currentConfig.BankAccountHolder = Input.PaymentMethod == "Cash" ? null : Input.BankAccountHolder?.Trim();
            }
            else
            {
                // Tạo mới cấu hình mặc định kèm thông tin thanh toán
                _context.EmployeeSalaryConfigs.Add(new EmployeeSalaryConfig
                {
                    EmployeeId = Input.EmployeeId,
                    BaseSalary = emp.BaseSalary,
                    TotalAllowances = emp.Allowance,
                    InsuranceSalary = emp.SocialInsuranceBase ?? emp.BaseSalary,
                    PenaltyPerLateMinute = emp.PenaltyPerLateMinute,
                    DependentCount = emp.DependentCount,
                    PaymentMethod = Input.PaymentMethod,
                    BankName = Input.PaymentMethod == "Cash" ? null : Input.BankName?.Trim(),
                    BankAccountNo = Input.PaymentMethod == "Cash" ? null : Input.BankAccountNo?.Trim(),
                    BankAccountHolder = Input.PaymentMethod == "Cash" ? null : Input.BankAccountHolder?.Trim(),
                    EffectiveFrom = DateTime.Today,
                    IsActive = true
                });
            }

            await _context.SaveChangesAsync();
            Message = $"Đã cập nhật thông tin thanh toán cho nhân viên {emp.FullName}.";
            IsError = false;
        }
        catch (Exception ex)
        {
            return SetError(ex.Message);
        }

        await LoadDataAsync();
        return Page();
    }

    private async Task LoadDataAsync()
    {
        Branches = await _context.Branches.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();

        var query = _context.Employees.AsNoTracking().Where(x => x.IsActive).AsQueryable();

        if (BranchId.HasValue && BranchId > 0)
            query = query.Where(x => x.BranchId == BranchId.Value);

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim().ToLower();
            query = query.Where(x => x.FullName.ToLower().Contains(term) || x.EmployeeCode.ToLower().Contains(term));
        }

        var list = await query.OrderBy(x => x.EmployeeCode).ToListAsync();

        var configMap = await _context.EmployeeSalaryConfigs.AsNoTracking()
            .Where(x => x.IsActive)
            .GroupBy(x => x.EmployeeId)
            .Select(g => g.OrderByDescending(x => x.EffectiveFrom).First())
            .ToDictionaryAsync(x => x.EmployeeId);

        Employees = list.Select(e =>
        {
            configMap.TryGetValue(e.Id, out var cfg);
            return new EmployeePaymentItem
            {
                EmployeeId = e.Id,
                EmployeeCode = e.EmployeeCode,
                FullName = e.FullName,
                BranchName = e.Branch?.Name ?? "—",
                PaymentMethod = cfg?.PaymentMethod ?? "BankTransfer",
                BankName = cfg?.BankName,
                BankAccountNo = cfg?.BankAccountNo,
                BankAccountHolder = string.IsNullOrWhiteSpace(cfg?.BankAccountHolder) ? e.FullName : cfg.BankAccountHolder
            };
        }).ToList();
    }

    private IActionResult SetError(string message)
    {
        Message = message; IsError = true; return Page();
    }

    public class EmployeePaymentItem
    {
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = "";
        public string FullName { get; set; } = "";
        public string BranchName { get; set; } = "";
        public string PaymentMethod { get; set; } = "BankTransfer";
        public string? BankName { get; set; }
        public string? BankAccountNo { get; set; }
        public string? BankAccountHolder { get; set; }
    }

    public class PaymentUpdateInput
    {
        public int EmployeeId { get; set; }
        public string PaymentMethod { get; set; } = "BankTransfer";
        public string? BankName { get; set; }
        public string? BankAccountNo { get; set; }
        public string? BankAccountHolder { get; set; }
    }
}