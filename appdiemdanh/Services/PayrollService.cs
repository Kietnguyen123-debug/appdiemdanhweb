using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AppDiemDanh.Services;

public interface IPayrollService
{
    Task<PayslipResponse> CalculateMonthlyPayslipAsync(int employeeId, int month, int year, int standardWorkDays);
    Task<int> FinalizeMonthlyPayrollAsync(int month, int year, int standardWorkDays);
    Task UpdateSalaryConfigAsync(int employeeId, UpdateSalaryConfigRequest request);
}

public class PayrollService : IPayrollService
{
    private readonly AppDbContext _context;
    private readonly IPayrollPolicyService _payrollPolicyService;
    private readonly IFcmService _fcmService;
    private readonly ILatePenaltyService _latePenaltyService;
    private readonly ILogger<PayrollService> _logger;
    private readonly PayrollCalculationEngine _payrollCalculationEngine;
    private readonly IHybridScheduleService _hybridScheduleService;
    private readonly PayrollWorkdayCalculator _payrollWorkdayCalculator;

    public PayrollService(
        AppDbContext context,
        IPayrollPolicyService payrollPolicyService,
        IFcmService fcmService,
        ILatePenaltyService latePenaltyService,
        ILogger<PayrollService> logger,
        PayrollCalculationEngine payrollCalculationEngine,
        IHybridScheduleService hybridScheduleService,
        PayrollWorkdayCalculator payrollWorkdayCalculator)
    {
        _context = context;
        _payrollPolicyService = payrollPolicyService;
        _fcmService = fcmService;
        _latePenaltyService = latePenaltyService;
        _logger = logger;
        _payrollCalculationEngine = payrollCalculationEngine;
        _hybridScheduleService = hybridScheduleService;
        _payrollWorkdayCalculator = payrollWorkdayCalculator;
    }

    public async Task<PayslipResponse> CalculateMonthlyPayslipAsync(int employeeId, int month, int year, int standardWorkDays)
    {
        if (month is < 1 or > 12 || year < 1 || standardWorkDays <= 0)
            throw new ArgumentException("Thông tin tháng, năm hoặc số ngày công chuẩn không hợp lệ.");

        var employee = await _context.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId);
        if (employee == null) throw new KeyNotFoundException("Không tìm thấy nhân viên.");
        return await CalculatePayslipAsync(employee, month, year, standardWorkDays);
    }

    public async Task<int> FinalizeMonthlyPayrollAsync(int month, int year, int standardWorkDays)
    {
        var employees = await _context.Employees.Where(x => x.IsActive).ToListAsync();
        foreach (var employee in employees)
        {
            var payslip = await CalculatePayslipAsync(employee, month, year, standardWorkDays);
            var snapshot = await _context.PayrollSnapshots.FirstOrDefaultAsync(x => x.EmployeeId == employee.Id && x.Month == month && x.Year == year);

            if (snapshot == null)
            {
                _context.PayrollSnapshots.Add(new PayrollSnapshot
                {
                    EmployeeId = employee.Id,
                    EmployeeCode = employee.EmployeeCode,
                    FullName = employee.FullName,
                    Month = month,
                    Year = year,
                    StandardWorkDays = standardWorkDays,
                    ActualWorkDays = payslip.ActualWorkDays,
                    BaseSalary = payslip.BaseSalary,
                    GrossSalary = payslip.GrossSalary,
                    OvertimeSalary = payslip.OvertimeSalary,
                    Allowance = payslip.Allowance,
                    InsuranceDeduction = payslip.InsuranceDeduction,
                    Pit = payslip.Pit,
                    SalaryAdvanceDeduction = payslip.SalaryAdvanceDeduction,
                    NetSalary = payslip.NetSalary,

                    // Lưu vết thông tin thanh toán vào Snapshot
                    PaymentMethod = payslip.PaymentMethod,
                    BankName = payslip.BankName,
                    BankAccountNo = payslip.BankAccountNo,
                    BankAccountHolder = payslip.BankAccountHolder,

                    Status = "Approved",
                    PayslipJson = JsonSerializer.Serialize(payslip),
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                snapshot.EmployeeCode = employee.EmployeeCode;
                snapshot.FullName = employee.FullName;
                snapshot.StandardWorkDays = standardWorkDays;
                snapshot.ActualWorkDays = payslip.ActualWorkDays;
                snapshot.BaseSalary = payslip.BaseSalary;
                snapshot.GrossSalary = payslip.GrossSalary;
                snapshot.OvertimeSalary = payslip.OvertimeSalary;
                snapshot.Allowance = payslip.Allowance;
                snapshot.InsuranceDeduction = payslip.InsuranceDeduction;
                snapshot.Pit = payslip.Pit;
                snapshot.SalaryAdvanceDeduction = payslip.SalaryAdvanceDeduction;
                snapshot.NetSalary = payslip.NetSalary;

                // Cập nhật lại thông tin thanh toán khi chốt lại
                snapshot.PaymentMethod = payslip.PaymentMethod;
                snapshot.BankName = payslip.BankName;
                snapshot.BankAccountNo = payslip.BankAccountNo;
                snapshot.BankAccountHolder = payslip.BankAccountHolder;

                snapshot.PayslipJson = JsonSerializer.Serialize(payslip);
                snapshot.CreatedAt = DateTime.UtcNow;
            }

            try
            {
                var paymentTypeText = payslip.PaymentMethod == "BankTransfer" ? "Chuyển khoản" : "Tiền mặt";
                await _fcmService.SendNotificationToEmployeeAsync(
                    employee.Id,
                    $"Bảng lương tháng {month:00}/{year}",
                    $"Bảng lương đã chốt. Lương thực nhận: {payslip.NetSalary:N0} VNĐ ({paymentTypeText}).",
                    "Payroll"
                );
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not send payroll notification for employee {EmployeeId}.", employee.Id);
            }
        }

        await _context.SaveChangesAsync();
        return employees.Count;
    }

    public async Task UpdateSalaryConfigAsync(int employeeId, UpdateSalaryConfigRequest request)
    {
        if (request.EffectiveFrom == default)
            throw new ArgumentException("Ngày hiệu lực là bắt buộc.");

        var effectiveFrom = request.EffectiveFrom.Date;
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null) throw new KeyNotFoundException("Không tìm thấy nhân viên.");

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var current = await _context.EmployeeSalaryConfigs
            .Where(x => x.EmployeeId == employeeId && x.IsActive)
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync();

        var overlapping = await _context.EmployeeSalaryConfigs
            .Where(x => x.EmployeeId == employeeId && x.EffectiveFrom <= effectiveFrom &&
                        (x.EffectiveTo == null || x.EffectiveTo >= effectiveFrom))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync();

        if (overlapping != null)
        {
            if (effectiveFrom <= overlapping.EffectiveFrom.Date)
                throw new ArgumentException("Ngày hiệu lực mới phải sau ngày hiệu lực gần nhất.");

            overlapping.EffectiveTo = effectiveFrom.AddDays(-1);
            overlapping.IsActive = false;
        }

        if (current != null && current != overlapping)
        {
            current.EffectiveTo = effectiveFrom.AddDays(-1);
            current.IsActive = false;
        }

        // Tạo bản ghi cấu hình lương & thông tin thanh toán mới
        _context.EmployeeSalaryConfigs.Add(new EmployeeSalaryConfig
        {
            EmployeeId = employeeId,
            BaseSalary = request.BaseSalary,
            TotalAllowances = request.Allowance,
            InsuranceSalary = request.SocialInsuranceBase.HasValue
                ? (decimal)request.SocialInsuranceBase.Value
                : request.BaseSalary,
            PenaltyPerLateMinute = request.PenaltyPerLateMinute,
            DependentCount = request.DependentCount,

            // Lưu thông tin thanh toán ngân hàng
            PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "BankTransfer" : request.PaymentMethod,
            BankName = request.PaymentMethod == "Cash" ? null : request.BankName?.Trim(),
            BankAccountNo = request.PaymentMethod == "Cash" ? null : request.BankAccountNo?.Trim(),
            BankAccountHolder = request.PaymentMethod == "Cash" ? null : request.BankAccountHolder?.Trim(),

            EffectiveFrom = effectiveFrom,
            IsActive = true
        });

        // Đồng bộ dữ liệu cũ trên bảng Employees
        employee.BaseSalary = request.BaseSalary;
        employee.Allowance = request.Allowance;
        employee.PenaltyPerLateMinute = request.PenaltyPerLateMinute;
        employee.DependentCount = request.DependentCount;
        employee.SocialInsuranceBase = request.SocialInsuranceBase.HasValue ? (decimal?)request.SocialInsuranceBase.Value : null;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private async Task<PayslipResponse> CalculatePayslipAsync(Employee employee, int month, int year, int standardWorkDays)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1);
        var policy = await _payrollPolicyService.GetActivePolicy(startDate)
            ?? throw new PayrollPolicyUnavailableException(startDate);

        var legacySalaryConfig = await _context.EmployeeSalaryConfigs.AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id && x.EffectiveFrom <= startDate &&
                        (x.EffectiveTo == null || x.EffectiveTo >= startDate))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync();

        legacySalaryConfig ??= new EmployeeSalaryConfig
        {
            EmployeeId = employee.Id,
            BaseSalary = employee.BaseSalary,
            TotalAllowances = employee.Allowance,
            InsuranceSalary = employee.SocialInsuranceBase ?? employee.BaseSalary,
            PenaltyPerLateMinute = employee.PenaltyPerLateMinute,
            DependentCount = employee.DependentCount,
            PaymentMethod = "BankTransfer",
            EffectiveFrom = DateTime.MinValue,
            IsActive = true
        };

        var salaryRevision = await _context.EmployeeSalaries.AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id && x.Status == "Active" && x.EffectiveDate <= startDate &&
                        (x.EndDate == null || x.EndDate >= startDate))
            .OrderByDescending(x => x.EffectiveDate)
            .Include(x => x.Details)
                .ThenInclude(x => x.SalaryComponent)
            .Include(x => x.SalaryTemplate)
                .ThenInclude(x => x!.Details)
                    .ThenInclude(x => x.SalaryComponent)
            .FirstOrDefaultAsync();

        var attendances = await _context.Attendances.AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id && x.WorkDate >= startDate && x.WorkDate < endDate)
            .ToListAsync();

        var overtime = await _context.OvertimeRecords.AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id && x.WorkDate >= startDate && x.WorkDate < endDate && x.Status == "Approved")
            .ToListAsync();

        var advances = await _context.SalaryAdvances.AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id && x.RequestDate >= startDate && x.RequestDate < endDate && x.Status == "Approved")
            .SumAsync(x => (decimal?)x.Amount) ?? 0;

        var paidLeaveRequests = await _context.LeaveRequests.AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id && x.Status == "APPROVED" && x.IsPaidLeave &&
                        x.StartDate < endDate && x.EndDate >= startDate)
            .Select(x => new { x.StartDate, x.EndDate, x.IsPaidLeave })
            .ToListAsync();

        var roster = await _hybridScheduleService.ResolveAsync(new RosterQuery { From = startDate, To = endDate.AddDays(-1) });
        var resolvedWorkDates = roster.Employees.FirstOrDefault(x => x.EmployeeId == employee.Id)?.Days
            .Where(x => x.ShiftId.HasValue)
            .Select(x => x.WorkDate)
            .ToList() ?? [];
        IEnumerable<DateTime> scheduledWorkDates = resolvedWorkDates.Count == 0
            ? _payrollWorkdayCalculator.CreateLegacyWeekdaySchedule(startDate, endDate.AddDays(-1))
            : resolvedWorkDates;
        var holidayDates = await _context.PayrollHolidays.AsNoTracking()
            .Where(x => x.IsActive && x.HolidayDate >= startDate && x.HolidayDate < endDate)
            .Select(x => x.HolidayDate)
            .ToListAsync();
        var workdays = _payrollWorkdayCalculator.Calculate(
            scheduledWorkDates,
            attendances.Where(x => x.CheckInTime != default).Select(x => x.WorkDate),
            paidLeaveRequests.Select(x => new PayrollLeavePeriod(x.StartDate, x.EndDate, x.IsPaidLeave)),
            holidayDates);
        decimal actualWorkDays = workdays.ActualWorkDays;
        decimal paidLeaveDays = workdays.PaidLeaveDays;
        decimal totalInsuranceDays = workdays.TotalInsuranceDays;
        var approvedOvertime = overtime;
        var componentInputs = ResolveSalaryComponents(salaryRevision, legacySalaryConfig);
        var formulaVariables = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["BASE_SALARY"] = legacySalaryConfig.BaseSalary,
            ["SALARY_BASE"] = legacySalaryConfig.BaseSalary,
            ["ACTUAL_WORK_DAYS"] = actualWorkDays,
            ["PAID_LEAVE_DAYS"] = paidLeaveDays,
            ["SYS_WORKING_DAYS"] = standardWorkDays,
            ["STANDARD_WORK_DAYS"] = standardWorkDays,
            ["OT_HOURS"] = approvedOvertime.Sum(x => (decimal)x.Hours)
        };
        var evaluatedComponents = _payrollCalculationEngine.EvaluateComponents(componentInputs, formulaVariables);
        var earningComponents = evaluatedComponents.Where(x => string.Equals(x.Definition.Category, "Earning", StringComparison.OrdinalIgnoreCase)).ToList();
        var baseSalary = earningComponents.FirstOrDefault(x => string.Equals(x.Definition.Code, "SALARY_BASE", StringComparison.OrdinalIgnoreCase))?.Amount
                         ?? legacySalaryConfig.BaseSalary;
        var totalAllowances = salaryRevision == null
            ? legacySalaryConfig.TotalAllowances
            : earningComponents.Where(x => !string.Equals(x.Definition.Code, "SALARY_BASE", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount);
        bool subjectToInsurance = totalInsuranceDays >= 14m;
        decimal configuredInsuranceSalary = salaryRevision == null
            ? legacySalaryConfig.InsuranceSalary
            : earningComponents.Where(x => string.Equals(x.Definition.InsuranceTreatment, "Included", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount);
        decimal insuranceBase = Math.Clamp(configuredInsuranceSalary, policy.ReferenceInsuranceSalary, policy.InsuranceCeiling);
        decimal employeeInsuranceRate = policy.EmployeeSocialInsuranceRate + policy.EmployeeHealthInsuranceRate + policy.EmployeeUnemploymentInsuranceRate;
        decimal employerInsuranceRate = policy.EmployerSocialInsuranceRate + policy.EmployerHealthInsuranceRate + policy.EmployerUnemploymentInsuranceRate;
        decimal employeeInsurance = subjectToInsurance ? Math.Round(insuranceBase * employeeInsuranceRate, 0) : 0m;
        decimal employerInsurance = subjectToInsurance ? Math.Round(insuranceBase * employerInsuranceRate, 0) : 0m;

        decimal dailyRate = baseSalary / standardWorkDays;
        decimal actualSalary = Math.Round(dailyRate * (actualWorkDays + paidLeaveDays), 0);
        decimal hourlyRate = dailyRate / 8m;
        decimal overtimeSalary = Math.Round(overtime.Sum(x => (decimal)x.Hours * (decimal)x.Multiplier * hourlyRate), 0);
        decimal overtimeOriginal = Math.Round(overtime.Sum(x => (decimal)x.Hours * hourlyRate), 0);
        decimal overtimeExempt = Math.Max(0, overtimeSalary - overtimeOriginal);

        decimal taxableGross = earningComponents.Sum(component =>
        {
            var amount = string.Equals(component.Definition.Code, "SALARY_BASE", StringComparison.OrdinalIgnoreCase)
                ? actualSalary
                : component.Amount;
            return _payrollCalculationEngine.CalculateTaxableAmount(component, amount, policy.MealTaxExemptLimit);
        }) + overtimeOriginal;
        decimal componentDeductions = evaluatedComponents
            .Where(x => string.Equals(x.Definition.Category, "Deduction", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Amount);
        bool isCasualIncome = salaryRevision?.IsCasualIncome == true;
        decimal taxableIncome = isCasualIncome
            ? taxableGross
            : Math.Max(0, taxableGross - policy.PersonalDeduction - (policy.DependentDeduction * legacySalaryConfig.DependentCount) - employeeInsurance);
        var taxDetails = isCasualIncome
            ? new PayrollTaxCalculation(
                _payrollCalculationEngine.CalculateCasualWithholding(taxableGross, policy.CasualIncomeWithholdingThreshold, policy.CasualIncomeWithholdingRate),
                Array.Empty<ProgressiveTaxBracketDto>())
            : _payrollCalculationEngine.CalculateProgressiveTax(taxableIncome, policy.TaxBrackets
                .OrderBy(x => x.Sequence)
                .Select(x => new PayrollTaxBracketInput(x.UpperLimit, x.Rate, x.Name)));

        decimal penalty = 0m;
        foreach (var attendance in attendances.Where(x => x.LateMinutes > 0))
            penalty += await _latePenaltyService.CalculatePenaltyAmountAsync(attendance.LateMinutes);

        decimal net = actualSalary + overtimeSalary + totalAllowances - employeeInsurance - taxDetails.Tax - componentDeductions - advances - penalty;

        return new PayslipResponse
        {
            EmployeeId = employee.Id,
            EmployeeCode = employee.EmployeeCode,
            FullName = employee.FullName,
            Month = month,
            Year = year,
            BaseSalary = baseSalary,
            Allowance = totalAllowances,
            StandardWorkDays = standardWorkDays,
            ActualWorkDays = actualWorkDays,
            TotalLateMinutes = attendances.Sum(x => x.LateMinutes),
            TotalPenalty = penalty,
            GrossSalary = actualSalary + overtimeSalary + totalAllowances,
            OvertimeSalary = overtimeSalary,
            OvertimeTaxExemptAmount = overtimeExempt,
            InsuranceBase = insuranceBase,
            InsuranceDeduction = employeeInsurance,
            EmployerInsuranceAmount = employerInsurance,
            PaidLeaveDays = paidLeaveDays,
            TotalInsuranceDays = totalInsuranceDays,
            SubjectToInsurance = subjectToInsurance,
            TaxableIncome = taxableIncome,
            IsCasualIncome = isCasualIncome,
            PersonalDeduction = policy.PersonalDeduction,
            DependentDeduction = policy.DependentDeduction * legacySalaryConfig.DependentCount,
            DependentCount = legacySalaryConfig.DependentCount,
            Pit = taxDetails.Tax,
            ComponentDeductions = componentDeductions,
            SalaryAdvanceDeduction = advances,
            NetSalary = net,
            TaxBrackets = taxDetails.Brackets,
            PolicyEffectiveDate = policy.EffectiveDate,

            // Trả về thông tin thanh toán cho Bảng lương & Excel Export
            PaymentMethod = legacySalaryConfig.PaymentMethod ?? "BankTransfer",
            BankName = legacySalaryConfig.BankName,
            BankAccountNo = legacySalaryConfig.BankAccountNo,
            BankAccountHolder = string.IsNullOrWhiteSpace(legacySalaryConfig.BankAccountHolder) ? employee.FullName : legacySalaryConfig.BankAccountHolder
        };
    }

    private static IReadOnlyList<PayrollComponentInput> ResolveSalaryComponents(EmployeeSalary? salaryRevision, EmployeeSalaryConfig legacySalaryConfig)
    {
        if (salaryRevision == null)
        {
            return
            [
                new("SALARY_BASE", "Lương cơ bản", "Earning", "Manual", null, "Taxable", null, "Included", legacySalaryConfig.BaseSalary),
                new("LEGACY_ALLOWANCE", "Phụ cấp", "Earning", "Fixed", null, "Taxable", null, "Excluded", legacySalaryConfig.TotalAllowances)
            ];
        }

        var assignedDetails = salaryRevision.Details.ToDictionary(x => x.SalaryComponentId);
        var templateDetails = salaryRevision.SalaryTemplate?.Details ?? [];
        var components = templateDetails.Select(templateDetail =>
        {
            var component = templateDetail.SalaryComponent ?? throw new InvalidOperationException("Mẫu lương có thành phần không tồn tại trong catalog.");
            assignedDetails.TryGetValue(templateDetail.SalaryComponentId, out var assignedDetail);
            var formula = assignedDetail?.Formula ?? templateDetail.Formula ?? component.Formula;
            var amount = assignedDetail?.Amount ?? templateDetail.DefaultValue;
            if (amount == null && string.Equals(component.Code, "SALARY_BASE", StringComparison.OrdinalIgnoreCase))
                amount = legacySalaryConfig.BaseSalary;

            var calculationType = formula == null ? component.CalculationType : "Formula";
            return new PayrollComponentInput(component.Code, component.Name, component.Category, calculationType, formula,
                component.TaxTreatment, component.TaxExemptLimit, component.InsuranceTreatment, amount);
        }).ToList();

        foreach (var assignedDetail in salaryRevision.Details.Where(x => !templateDetails.Any(t => t.SalaryComponentId == x.SalaryComponentId)))
        {
            var component = assignedDetail.SalaryComponent ?? throw new InvalidOperationException("Cấu hình lương có thành phần không tồn tại trong catalog.");
            var formula = assignedDetail.Formula ?? component.Formula;
            components.Add(new PayrollComponentInput(component.Code, component.Name, component.Category,
                formula == null ? component.CalculationType : "Formula", formula, component.TaxTreatment,
                component.TaxExemptLimit, component.InsuranceTreatment, assignedDetail.Amount));
        }

        return components.Where(x =>
            !string.Equals(x.Category, "Deduction", StringComparison.OrdinalIgnoreCase) ||
            (!string.Equals(x.Code, "INSURANCE_EMP", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(x.Code, "PERSONAL_TAX", StringComparison.OrdinalIgnoreCase))).ToList();
    }

}

public sealed class PayrollPolicyUnavailableException : InvalidOperationException
{
    public PayrollPolicyUnavailableException(DateTime payPeriodDate)
        : base($"Chưa có chính sách Thuế/BHXH có hiệu lực cho kỳ lương bắt đầu ngày {payPeriodDate:yyyy-MM-dd}.")
    {
    }
}
