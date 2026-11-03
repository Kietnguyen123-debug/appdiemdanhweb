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

    public PayrollService(
        AppDbContext context,
        IPayrollPolicyService payrollPolicyService,
        IFcmService fcmService,
        ILatePenaltyService latePenaltyService,
        ILogger<PayrollService> logger)
    {
        _context = context;
        _payrollPolicyService = payrollPolicyService;
        _fcmService = fcmService;
        _latePenaltyService = latePenaltyService;
        _logger = logger;
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
        var policy = await _payrollPolicyService.GetActivePolicy(startDate);

        var salaryConfig = await _context.EmployeeSalaryConfigs.AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id && x.EffectiveFrom <= startDate &&
                        (x.EffectiveTo == null || x.EffectiveTo >= startDate))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync();

        salaryConfig ??= new EmployeeSalaryConfig
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

        var attendances = await _context.Attendances.AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id && x.WorkDate >= startDate && x.WorkDate < endDate)
            .ToListAsync();

        var overtime = await _context.OvertimeRecords.AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id && x.WorkDate >= startDate && x.WorkDate < endDate && x.Status == "Approved")
            .ToListAsync();

        var advances = await _context.SalaryAdvances.AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id && x.RequestDate >= startDate && x.RequestDate < endDate && x.Status == "Approved")
            .SumAsync(x => (decimal?)x.Amount) ?? 0;

        decimal actualWorkDays = attendances.Count(x => x.CheckInTime != default);
        var paidLeaveRequests = await _context.LeaveRequests.AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id && x.Status == "APPROVED" && x.IsPaidLeave &&
                        x.StartDate < endDate && x.EndDate >= startDate)
            .Select(x => new { x.StartDate, x.EndDate })
            .ToListAsync();

        decimal paidLeaveDays = paidLeaveRequests.Sum(x =>
        {
            var from = x.StartDate.Date < startDate ? startDate : x.StartDate.Date;
            var to = x.EndDate.Date >= endDate ? endDate.AddDays(-1) : x.EndDate.Date;
            return to >= from ? (decimal)(to - from).TotalDays + 1 : 0m;
        });

        decimal totalInsuranceDays = actualWorkDays + paidLeaveDays;
        bool subjectToInsurance = totalInsuranceDays >= 14m;
        decimal insuranceBase = Math.Clamp(salaryConfig.InsuranceSalary, policy.ReferenceInsuranceSalary, policy.InsuranceCeiling);
        decimal employeeInsuranceRate = policy.EmployeeSocialInsuranceRate + policy.EmployeeHealthInsuranceRate + policy.EmployeeUnemploymentInsuranceRate;
        decimal employerInsuranceRate = policy.EmployerSocialInsuranceRate + policy.EmployerHealthInsuranceRate + policy.EmployerUnemploymentInsuranceRate;
        decimal employeeInsurance = subjectToInsurance ? Math.Round(insuranceBase * employeeInsuranceRate, 0) : 0m;
        decimal employerInsurance = subjectToInsurance ? Math.Round(insuranceBase * employerInsuranceRate, 0) : 0m;

        decimal dailyRate = salaryConfig.BaseSalary / standardWorkDays;
        decimal actualSalary = Math.Round(dailyRate * actualWorkDays, 0);
        decimal hourlyRate = dailyRate / 8m;
        decimal overtimeSalary = Math.Round(overtime.Sum(x => (decimal)x.Hours * (decimal)x.Multiplier * hourlyRate), 0);
        decimal overtimeOriginal = Math.Round(overtime.Sum(x => (decimal)x.Hours * hourlyRate), 0);
        decimal overtimeExempt = Math.Max(0, overtimeSalary - overtimeOriginal);

        decimal taxableIncome = Math.Max(0, actualSalary + salaryConfig.TotalAllowances + overtimeOriginal -
            policy.PersonalDeduction - (policy.DependentDeduction * salaryConfig.DependentCount) - employeeInsurance);
        var taxDetails = CalculateTax(taxableIncome, policy.TaxBrackets);

        decimal penalty = 0m;
        foreach (var attendance in attendances.Where(x => x.LateMinutes > 0))
            penalty += await _latePenaltyService.CalculatePenaltyAmountAsync(attendance.LateMinutes);

        decimal net = actualSalary + overtimeSalary + salaryConfig.TotalAllowances - employeeInsurance - taxDetails.Tax - advances - penalty;

        return new PayslipResponse
        {
            EmployeeId = employee.Id,
            EmployeeCode = employee.EmployeeCode,
            FullName = employee.FullName,
            Month = month,
            Year = year,
            BaseSalary = salaryConfig.BaseSalary,
            Allowance = salaryConfig.TotalAllowances,
            StandardWorkDays = standardWorkDays,
            ActualWorkDays = actualWorkDays,
            TotalLateMinutes = attendances.Sum(x => x.LateMinutes),
            TotalPenalty = penalty,
            GrossSalary = actualSalary + overtimeSalary + salaryConfig.TotalAllowances,
            OvertimeSalary = overtimeSalary,
            OvertimeTaxExemptAmount = overtimeExempt,
            InsuranceBase = insuranceBase,
            InsuranceDeduction = employeeInsurance,
            EmployerInsuranceAmount = employerInsurance,
            PaidLeaveDays = paidLeaveDays,
            TotalInsuranceDays = totalInsuranceDays,
            SubjectToInsurance = subjectToInsurance,
            TaxableIncome = taxableIncome,
            PersonalDeduction = policy.PersonalDeduction,
            DependentDeduction = policy.DependentDeduction * salaryConfig.DependentCount,
            DependentCount = salaryConfig.DependentCount,
            Pit = taxDetails.Tax,
            SalaryAdvanceDeduction = advances,
            NetSalary = net,
            TaxBrackets = taxDetails.Brackets,
            PolicyEffectiveDate = policy.EffectiveDate,

            // Trả về thông tin thanh toán cho Bảng lương & Excel Export
            PaymentMethod = salaryConfig.PaymentMethod ?? "BankTransfer",
            BankName = salaryConfig.BankName,
            BankAccountNo = salaryConfig.BankAccountNo,
            BankAccountHolder = string.IsNullOrWhiteSpace(salaryConfig.BankAccountHolder) ? employee.FullName : salaryConfig.BankAccountHolder
        };
    }

    private static (decimal Tax, List<ProgressiveTaxBracketDto> Brackets) CalculateTax(decimal income, IEnumerable<PayrollTaxBracket> policyBrackets)
    {
        var result = new List<ProgressiveTaxBracketDto>();
        decimal previous = 0, total = 0;
        foreach (var bracket in policyBrackets.OrderBy(x => x.Sequence))
        {
            decimal upperLimit = bracket.UpperLimit ?? decimal.MaxValue;
            decimal amount = Math.Max(0, Math.Min(income, upperLimit) - previous);
            if (amount > 0)
            {
                var tax = Math.Round(amount * bracket.Rate, 0);
                result.Add(new ProgressiveTaxBracketDto { Bracket = bracket.Name, TaxableAmount = amount, Rate = bracket.Rate, TaxAmount = tax });
                total += tax;
            }
            if (income <= upperLimit) break;
            previous = upperLimit;
        }
        return (total, result);
    }
}