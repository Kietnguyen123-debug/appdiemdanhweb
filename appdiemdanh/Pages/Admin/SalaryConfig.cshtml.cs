using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using AppDiemDanh.Services;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Pages.Admin;

public class SalaryConfigModel : AdminPageModel
{
    private readonly AppDbContext _context;
    private readonly IPayrollService _payrollService;
    private readonly IPayrollPolicyService _payrollPolicyService;

    public List<Branch> Branches { get; private set; } = [];
    public List<Department> Departments { get; private set; } = [];
    public List<EmployeeOption> Employees { get; private set; } = [];
    public EmployeeSalaryValues? SelectedEmployee { get; private set; }
    public IReadOnlyList<SalaryComponentOption> SalaryComponents { get; private set; } =
    [
        new(0, "Lương cơ bản", "Cộng vào lương", "Nhập tay", "Chịu thuế", "Tính đóng BHXH"),
        new(0, "Phụ cấp ăn trưa", "Cộng vào lương", "Cố định", "Miễn thuế có định mức", "Không tính đóng BHXH"),
        new(0, "Phụ cấp trách nhiệm", "Cộng vào lương", "Cố định", "Chịu thuế", "Tính đóng BHXH"),
        new(0, "Thưởng KPI", "Cộng vào lương", "Công thức động", "Chịu thuế", "Không tính đóng BHXH"),
        new(0, "BHXH NLĐ", "Khấu trừ", "Công thức động", "Không áp dụng", "Không tính đóng BHXH"),
        new(0, "Thuế TNCN", "Khấu trừ", "Công thức động", "Không áp dụng", "Không tính đóng BHXH")
    ];
    public IReadOnlyList<SalaryTemplateOption> SalaryTemplates { get; private set; } =
    [
        new(0, "TPL_TECH_SENIOR", "Tech Senior", "Kỹ thuật", 3),
        new(0, "TPL_SALES_SPECIALIST", "Sales Specialist", "Kinh doanh", 3),
        new(0, "TPL_OFFICE", "Khối văn phòng", "Vận hành", 3)
    ];
    public LegalDefaultsConfig LegalDefaults { get; } = new();
    public PayrollPolicy? ActivePolicy { get; private set; }
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    [BindProperty]
    public ComponentFormModel ComponentForm { get; set; } = new();

    [BindProperty]
    public TemplateFormModel TemplateForm { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? BranchId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? EmployeeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ActiveTab { get; set; } = "global";

    [BindProperty(SupportsGet = true)]
    public string ActiveSubtab { get; set; } = "components";

    [BindProperty]
    public UpdateSalaryConfigRequest Form { get; set; } = new();

    [BindProperty]
    public SavePayrollPolicyRequest PolicyForm { get; set; } = new();

    public SalaryConfigModel(AppDbContext context, IPayrollService payrollService, IPayrollPolicyService payrollPolicyService)
    {
        _context = context;
        _payrollService = payrollService;
        _payrollPolicyService = payrollPolicyService;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsAdminSession)
            return RedirectToPage("/Admin/Login");

        await LoadAsync();
        await LoadSalaryCatalogAsync();
        await LoadPolicyAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateComponentAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (string.IsNullOrWhiteSpace(ComponentForm.Code) || string.IsNullOrWhiteSpace(ComponentForm.Name))
            return await SetErrorAsync("Vui lòng nhập mã và tên thành phần lương.");

        try
        {
            _context.SalaryComponents.Add(new SalaryComponent
            {
                Code = ComponentForm.Code.Trim(), Name = ComponentForm.Name.Trim(), Category = ComponentForm.Category,
                CalculationType = ComponentForm.CalculationType, TaxTreatment = ComponentForm.TaxTreatment,
                InsuranceTreatment = ComponentForm.InsuranceTreatment, TaxExemptLimit = ComponentForm.TaxExemptLimit
            });
            await _context.SaveChangesAsync();
            Message = "Đã thêm thành phần lương.";
            IsError = false;
        }
        catch (DbException) { return await SetErrorAsync("Chưa có bảng SalaryComponents. Hãy chạy docs/salary-configuration-schema.sql trước."); }
        catch (DbUpdateException exception)
        {
            _context.Entry(_context.SalaryComponents.Local.Last()).State = EntityState.Detached;
            return await SetErrorAsync(GetSalaryWriteError("thành phần", exception));
        }

        await LoadAsync(); await LoadSalaryCatalogAsync(); await LoadPolicyAsync(); return Page();
    }

    public async Task<IActionResult> OnPostDeleteComponentAsync(long id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");

        var component = await _context.SalaryComponents.FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (component == null)
            return await SetErrorAsync("Không tìm thấy thành phần lương cần xóa.");

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var details = await _context.SalaryTemplateDetails.Where(x => x.SalaryComponentId == id).ToListAsync();
            _context.SalaryTemplateDetails.RemoveRange(details);
            _context.SalaryComponents.Remove(component);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            Message = "Đã xóa thành phần lương.";
            IsError = false;
        }
        catch (DbUpdateException)
        {
            return await SetErrorAsync("Không thể xóa thành phần này vì đang được dữ liệu lương khác tham chiếu.");
        }
        await LoadAsync(); await LoadSalaryCatalogAsync(); await LoadPolicyAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteTemplateAsync(long id)
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");

        var template = await _context.SalaryTemplates.FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (template == null)
            return await SetErrorAsync("Không tìm thấy mẫu lương cần xóa.");

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            _context.SalaryTemplates.Remove(template);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            Message = "Đã xóa mẫu lương.";
            IsError = false;
        }
        catch (DbUpdateException)
        {
            return await SetErrorAsync("Không thể xóa mẫu lương vì đang được dữ liệu lương khác tham chiếu.");
        }
        await LoadAsync(); await LoadSalaryCatalogAsync(); await LoadPolicyAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateTemplateAsync()
    {
        if (!IsAdminSession) return RedirectToPage("/Admin/Login");
        if (string.IsNullOrWhiteSpace(TemplateForm.Code) || string.IsNullOrWhiteSpace(TemplateForm.Name))
            return await SetErrorAsync("Vui lòng nhập mã và tên mẫu lương.");

        var template = new SalaryTemplate
        {
            Code = TemplateForm.Code.Trim(),
            Name = TemplateForm.Name.Trim(),
            PositionGroup = TemplateForm.PositionGroup?.Trim()
        };

        try
        {
            _context.SalaryTemplates.Add(template);
            await _context.SaveChangesAsync();
            return RedirectToAction("Detail", "SalaryTemplates", new { id = template.Id });
        }
        catch (DbException) { return await SetErrorAsync("Chưa có bảng SalaryTemplates. Hãy chạy docs/salary-configuration-schema.sql trước."); }
        catch (DbUpdateException exception)
        {
            _context.Entry(template).State = EntityState.Detached;
            return await SetErrorAsync(GetSalaryWriteError("mẫu lương", exception));
        }
    }

    public async Task<IActionResult> OnPostSavePolicyAsync()
    {
        if (!IsAdminSession)
            return RedirectToPage("/Admin/Login");
        if (CurrentEmployeeId <= 0)
            return SetError("Không xác định được người tạo policy.");

        try
        {
            await _payrollPolicyService.CreatePolicyAsync(PolicyForm, CurrentEmployeeId);
            Message = "Đã tạo policy Thuế/BHXH mới. Kỳ lương sau ngày hiệu lực sẽ tự động sử dụng policy này.";
            IsError = false;
        }

        catch (ArgumentException exception)
        {
            return SetError(exception.Message);
        }
        catch (DbException)
        {
            return SetError("Chưa có bảng PayrollPolicies/PayrollTaxBrackets. Hãy chạy script SQL policy trước.");
        }

        await LoadAsync();
        await LoadPolicyAsync();
        return Page();
    }

    private async Task LoadSalaryCatalogAsync()
    {
        try
        {
            SalaryComponents = await _context.SalaryComponents.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
                .Select(x => new SalaryComponentOption(x.Id, x.Name, x.Category, x.CalculationType, x.TaxTreatment, x.InsuranceTreatment)).ToListAsync();
            SalaryTemplates = await _context.SalaryTemplates.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
                .Select(x => new SalaryTemplateOption(x.Id, x.Code, x.Name, x.PositionGroup ?? "Chưa phân nhóm", x.Details.Count)).ToListAsync();
        }
        catch (DbException) { }
    }

    private async Task<IActionResult> SetErrorAsync(string message)
    {
        Message = message;
        IsError = true;
        await LoadAsync();
        await LoadSalaryCatalogAsync();
        await LoadPolicyAsync();
        return Page();
    }

    private static string GetSalaryWriteError(string itemName, DbUpdateException exception)
    {
        var databaseMessage = exception.InnerException?.Message ?? exception.Message;
        if (databaseMessage.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) ||
            databaseMessage.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            return "Database chưa có bảng SalaryComponents/SalaryTemplates. Hãy chạy toàn bộ file docs/salary-configuration-schema.sql trên đúng database đang chạy ứng dụng.";

        if (databaseMessage.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase))
            return "Bảng salary trong database chưa đúng phiên bản schema. Hãy cập nhật lại các cột theo docs/salary-configuration-schema.sql.";

        if (databaseMessage.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
            databaseMessage.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
            databaseMessage.Contains("UQ_", StringComparison.OrdinalIgnoreCase))
            return $"Mã {itemName} đã tồn tại. Hãy nhập mã khác.";

        return $"Không thể lưu {itemName}. Kiểm tra dữ liệu nhập và cấu trúc bảng Salary schema.";
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!IsAdminSession)
            return RedirectToPage("/Admin/Login");

        await LoadBranchesAsync();

        if (!EmployeeId.HasValue || EmployeeId <= 0)
            return SetError("Vui lòng chọn nhân viên.");

        var employee = await _context.Employees
            .AsNoTracking()
            .Where(x => x.Id == EmployeeId && (!BranchId.HasValue || x.BranchId == BranchId))
            .Select(x => new EmployeeSalaryValues
            {
                EmployeeId = x.Id,
                EmployeeCode = x.EmployeeCode,
                FullName = x.FullName,
                BranchName = x.Branch!.Name,
                BaseSalary = x.BaseSalary,
                Allowance = x.Allowance,
                PenaltyPerLateMinute = x.PenaltyPerLateMinute,
                DependentCount = x.DependentCount,
                SocialInsuranceBase = x.SocialInsuranceBase
            })
            .FirstOrDefaultAsync();

        if (employee == null)
            return SetError("Nhân viên không tồn tại hoặc không thuộc chi nhánh đã chọn.");

        if (Form.BaseSalary < 0 || Form.Allowance < 0 || Form.PenaltyPerLateMinute < 0 || Form.DependentCount is < 0 or > 20 || Form.SocialInsuranceBase < 0)
            return SetError("Thông tin cấu hình lương không hợp lệ.");

        try
        {
            await _payrollService.UpdateSalaryConfigAsync(employee.EmployeeId, Form);
            Message = "Đã cập nhật cấu hình lương cho nhân viên.";
            IsError = false;
        }

        catch (KeyNotFoundException exception)
        {
            return SetError(exception.Message);
        }

        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        await LoadBranchesAsync();
        await LoadDepartmentsAsync();

        var currentSalaries = _context.EmployeeSalaries.AsNoTracking()
            .Where(x => x.Status == "Active" && x.EffectiveDate <= DateTime.Today && (x.EndDate == null || x.EndDate >= DateTime.Today));

        Employees = await _context.Employees
            .AsNoTracking()
            .Where(x => !BranchId.HasValue || x.BranchId == BranchId)
            .OrderBy(x => x.EmployeeCode)
            .Select(x => new
            {
                Employee = x,
                BranchName = x.Branch!.Name,
                DepartmentName = x.Department == null ? "Chưa phân phòng ban" : x.Department.Name
            })
            .Select(x => new EmployeeOption
            {
                Id = x.Employee.Id,
                EmployeeCode = x.Employee.EmployeeCode,
                FullName = x.Employee.FullName,
                IsActive = x.Employee.IsActive,
                BranchName = x.BranchName,
                DepartmentName = x.DepartmentName,
                DepartmentId = x.Employee.DepartmentId,
                SalaryId = currentSalaries.Where(s => s.EmployeeId == x.Employee.Id).OrderByDescending(s => s.EffectiveDate).Select(s => (long?)s.Id).FirstOrDefault(),
                SalaryTemplateId = currentSalaries.Where(s => s.EmployeeId == x.Employee.Id).OrderByDescending(s => s.EffectiveDate).Select(s => s.SalaryTemplateId).FirstOrDefault(),
                SalaryTemplateName = currentSalaries.Where(s => s.EmployeeId == x.Employee.Id).OrderByDescending(s => s.EffectiveDate).Select(s => s.SalaryTemplate!.Name).FirstOrDefault()
            })
            .ToListAsync();

        if (!EmployeeId.HasValue)
            return;

        SelectedEmployee = await _context.Employees
            .AsNoTracking()
            .Where(x => x.Id == EmployeeId && (!BranchId.HasValue || x.BranchId == BranchId))
            .Select(x => new EmployeeSalaryValues
            {
                EmployeeId = x.Id,
                EmployeeCode = x.EmployeeCode,
                FullName = x.FullName,
                BranchName = x.Branch!.Name,
                BaseSalary = x.BaseSalary,
                Allowance = x.Allowance,
                PenaltyPerLateMinute = x.PenaltyPerLateMinute,
                DependentCount = x.DependentCount,
                SocialInsuranceBase = x.SocialInsuranceBase
            })
            .FirstOrDefaultAsync();

        var currentConfig = await _context.EmployeeSalaryConfigs
            .AsNoTracking()
            .Where(x => x.EmployeeId == EmployeeId && x.IsActive)
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync();

        if (SelectedEmployee != null && currentConfig != null)
        {
            SelectedEmployee.BaseSalary = currentConfig.BaseSalary;
            SelectedEmployee.Allowance = currentConfig.TotalAllowances;
            SelectedEmployee.PenaltyPerLateMinute = currentConfig.PenaltyPerLateMinute;
            SelectedEmployee.DependentCount = currentConfig.DependentCount;
            SelectedEmployee.SocialInsuranceBase = currentConfig.InsuranceSalary;
        }

        if (SelectedEmployee != null && Request.Method != "POST")
        {
            Form = new UpdateSalaryConfigRequest
            {
                BaseSalary = SelectedEmployee.BaseSalary,
                Allowance = SelectedEmployee.Allowance,
                PenaltyPerLateMinute = SelectedEmployee.PenaltyPerLateMinute,
                DependentCount = SelectedEmployee.DependentCount,
                SocialInsuranceBase = SelectedEmployee.SocialInsuranceBase.HasValue
                    ? (double?)SelectedEmployee.SocialInsuranceBase.Value
                    : null
            };
        }
    }

    private async Task LoadPolicyAsync()
    {
        try
        {
            ActivePolicy = await _payrollPolicyService.GetActivePolicy(DateTime.Today);
            LegalDefaults.Apply(ActivePolicy);
            PolicyForm = new SavePayrollPolicyRequest
            {
                EffectiveDate = DateTime.Today.AddMonths(1),
                ReferenceInsuranceSalary = ActivePolicy.ReferenceInsuranceSalary,
                InsuranceCeiling = ActivePolicy.InsuranceCeiling,
                EmployeeSocialInsuranceRate = ActivePolicy.EmployeeSocialInsuranceRate,
                EmployeeHealthInsuranceRate = ActivePolicy.EmployeeHealthInsuranceRate,
                EmployeeUnemploymentInsuranceRate = ActivePolicy.EmployeeUnemploymentInsuranceRate,
                EmployerSocialInsuranceRate = ActivePolicy.EmployerSocialInsuranceRate,
                EmployerHealthInsuranceRate = ActivePolicy.EmployerHealthInsuranceRate,
                EmployerUnemploymentInsuranceRate = ActivePolicy.EmployerUnemploymentInsuranceRate,
                PersonalDeduction = ActivePolicy.PersonalDeduction,
                DependentDeduction = ActivePolicy.DependentDeduction,
                CasualIncomeWithholdingThreshold = ActivePolicy.CasualIncomeWithholdingThreshold,
                CasualIncomeWithholdingRate = ActivePolicy.CasualIncomeWithholdingRate,
                MealTaxExemptLimit = ActivePolicy.MealTaxExemptLimit,
                SourceReference = ActivePolicy.SourceReference,
                TaxBrackets = ActivePolicy.TaxBrackets.OrderBy(x => x.Sequence).Select(x => new PayrollTaxBracketDto
                {
                    Sequence = x.Sequence,
                    UpperLimit = x.UpperLimit,
                    Rate = x.Rate,
                    Name = x.Name
                }).ToList()
            };
        }
        catch (InvalidOperationException)
        {
            ActivePolicy = null;
            PolicyForm.TaxBrackets = Enumerable.Range(1, 5)
                .Select(sequence => new PayrollTaxBracketDto { Sequence = sequence })
                .ToList();
        }
        catch (DbException)
        {
            ActivePolicy = null;
            PolicyForm.TaxBrackets = Enumerable.Range(1, 5)
                .Select(sequence => new PayrollTaxBracketDto { Sequence = sequence })
                .ToList();
            Message = "Chưa có bảng policy trong database. Hãy chạy script SQL policy trước khi tính lương.";
            IsError = true;
        }
    }

    private async Task LoadBranchesAsync()
    {
        Branches = await _context.Branches
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();
    }

    private async Task LoadDepartmentsAsync()
    {
        Departments = await _context.Departments
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();
    }
    private IActionResult SetError(string message)
    {
        Message = message;
        IsError = true;
        return Page();
    }

    public sealed class EmployeeOption
    {
        public int Id { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public int? DepartmentId { get; set; }
        public long? SalaryId { get; set; }
        public long? SalaryTemplateId { get; set; }
        public string? SalaryTemplateName { get; set; }
    }

    public sealed class EmployeeSalaryValues
    {
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public decimal BaseSalary { get; set; }
        public decimal Allowance { get; set; }
        public decimal PenaltyPerLateMinute { get; set; }
        public int DependentCount { get; set; }
        public decimal? SocialInsuranceBase { get; set; }
    }

    public sealed record SalaryComponentOption(long Id, string Name, string Category, string CalculationType, string TaxTreatment, string InsuranceTreatment);

    public sealed record SalaryTemplateOption(long Id, string Code, string Name, string Group, int ComponentCount);

    public sealed class ComponentFormModel
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "Earning";
        public string CalculationType { get; set; } = "Manual";
        public string TaxTreatment { get; set; } = "Taxable";
        public string InsuranceTreatment { get; set; } = "Included";
        public decimal? TaxExemptLimit { get; set; }
    }

    public sealed class TemplateFormModel
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? PositionGroup { get; set; }
    }

    public sealed class LegalDefaultsConfig
    {
        public DateTime EffectiveDate { get; private set; }
        public decimal ReferenceInsuranceSalary { get; private set; }
        public decimal InsuranceCeiling { get; private set; }
        public decimal PersonalDeduction { get; private set; }
        public decimal DependentDeduction { get; private set; }
        public decimal MealTaxExemptLimit { get; private set; }
        public IReadOnlyList<string> InsuranceRates { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> TaxBrackets { get; private set; } = Array.Empty<string>();

        public void Apply(PayrollPolicy policy)
        {
            EffectiveDate = policy.EffectiveDate;
            ReferenceInsuranceSalary = policy.ReferenceInsuranceSalary;
            InsuranceCeiling = policy.InsuranceCeiling;
            PersonalDeduction = policy.PersonalDeduction;
            DependentDeduction = policy.DependentDeduction;
            MealTaxExemptLimit = policy.MealTaxExemptLimit;
            InsuranceRates = [$"NLĐ {(policy.EmployeeSocialInsuranceRate + policy.EmployeeHealthInsuranceRate + policy.EmployeeUnemploymentInsuranceRate):P1}", $"NSDLĐ {(policy.EmployerSocialInsuranceRate + policy.EmployerHealthInsuranceRate + policy.EmployerUnemploymentInsuranceRate):P1}", "BHTN tối đa 20 lần lương tối thiểu vùng"];
            TaxBrackets = policy.TaxBrackets.OrderBy(x => x.Sequence).Select(x => $"{(x.UpperLimit.HasValue ? $"Đến {x.UpperLimit:N0}" : "Trên mức cuối")}: {x.Rate:P0}").ToList();
        }
    }
}
