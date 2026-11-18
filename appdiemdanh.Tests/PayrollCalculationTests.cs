using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using AppDiemDanh.Services;
using AppDiemDanh.Pages.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AppDiemDanh.Tests;

public sealed class PayrollCalculationTests
{
    private static readonly DateTime PeriodStart = new(2026, 7, 1);

    [Fact]
    public async Task CalculatesFromEffectiveSalaryComponentsAndAppliesTaxExemptions()
    {
        await using var context = CreateContext();
        await SeedEmployeeAndWorkdaysAsync(context, 15);
        var template = CreateTemplate(
            CreateComponent(1, "SALARY_BASE", "Earning", "Manual", "Taxable", "Included"),
            CreateComponent(2, "ALLOW_MEAL", "Earning", "Fixed", "CappedExempt", "Excluded", 730_000m),
            CreateComponent(3, "ALLOW_GIFT", "Earning", "Fixed", "Exempt", "Excluded"));
        await SeedSalaryRevisionAsync(context, template, new Dictionary<string, decimal?>
        {
            ["SALARY_BASE"] = 30_000_000m,
            ["ALLOW_MEAL"] = 2_000_000m,
            ["ALLOW_GIFT"] = 1_000_000m
        });

        var payslip = await CreatePayrollService(context, CreatePolicy()).CalculateMonthlyPayslipAsync(1, 7, 2026, 15);

        Assert.Equal(30_000_000m, payslip.BaseSalary);
        Assert.Equal(3_000_000m, payslip.Allowance);
        Assert.Equal(33_000_000m, payslip.GrossSalary);
        Assert.Equal(30_800_000m, payslip.TaxableIncome);
        Assert.Equal(3_080_000m, payslip.Pit);
        Assert.Equal(0m, payslip.InsuranceDeduction);
    }

    [Fact]
    public async Task FallsBackToLegacySalaryWhenNoEffectiveTemplateExists()
    {
        await using var context = CreateContext();
        await SeedEmployeeAndWorkdaysAsync(context, 15);
        context.EmployeeSalaryConfigs.Add(new EmployeeSalaryConfig
        {
            EmployeeId = 1,
            BaseSalary = 8_000_000m,
            TotalAllowances = 1_000_000m,
            InsuranceSalary = 8_000_000m,
            DependentCount = 0,
            PenaltyPerLateMinute = 0,
            EffectiveFrom = PeriodStart.AddYears(-1),
            IsActive = true
        });
        await context.SaveChangesAsync();

        var payslip = await CreatePayrollService(context, CreatePolicy()).CalculateMonthlyPayslipAsync(1, 7, 2026, 15);

        Assert.Equal(8_000_000m, payslip.BaseSalary);
        Assert.Equal(1_000_000m, payslip.Allowance);
        Assert.Equal(9_000_000m, payslip.GrossSalary);
        Assert.Equal(900_000m, payslip.Pit);
    }

    [Fact]
    public async Task UsesCasualWithholdingInsteadOfProgressiveDeductions()
    {
        await using var context = CreateContext();
        await SeedEmployeeAndWorkdaysAsync(context, 15);
        var template = CreateTemplate(CreateComponent(1, "SALARY_BASE", "Earning", "Manual", "Taxable", "Included"));
        await SeedSalaryRevisionAsync(context, template, new Dictionary<string, decimal?> { ["SALARY_BASE"] = 6_000_000m }, isCasualIncome: true);
        var policy = CreatePolicy();
        policy.PersonalDeduction = 10_000_000m;

        var payslip = await CreatePayrollService(context, policy).CalculateMonthlyPayslipAsync(1, 7, 2026, 15);

        Assert.True(payslip.IsCasualIncome);
        Assert.Equal(6_000_000m, payslip.TaxableIncome);
        Assert.Equal(600_000m, payslip.Pit);
    }

    [Fact]
    public void CasualWithholdingAppliesAtAndAboveThresholdOnly()
    {
        var engine = new PayrollCalculationEngine(new PayrollFormulaEvaluator());

        Assert.Equal(0m, engine.CalculateCasualWithholding(4_999_999m, 5_000_000m, 0.1m));
        Assert.Equal(500_000m, engine.CalculateCasualWithholding(5_000_000m, 5_000_000m, 0.1m));
    }

    [Fact]
    public void InsuredWorkdayCountUsesDistinctScheduledWeekdaysAndPaidLeave()
    {
        var calculator = new PayrollWorkdayCalculator();
        var scheduled = Enumerable.Range(0, 21).Select(offset => PeriodStart.AddDays(offset)).ToList();
        var holidays = new[] { new DateTime(2026, 7, 2) };
        var eligible = scheduled.Where(x => x.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !holidays.Contains(x.Date)).Take(14).ToList();
        var attendance = eligible.Take(13).Append(new DateTime(2026, 7, 4)).Append(new DateTime(2026, 7, 2));
        var leave = new[] { new PayrollLeavePeriod(eligible[12], eligible[13], true), new PayrollLeavePeriod(new DateTime(2026, 7, 4), new DateTime(2026, 7, 5), true) };

        var result = calculator.Calculate(scheduled, attendance, leave, holidays);

        Assert.Equal(13m, result.ActualWorkDays);
        Assert.Equal(1m, result.PaidLeaveDays);
        Assert.Equal(14m, result.TotalInsuranceDays);
        Assert.True(result.TotalInsuranceDays >= 14m);
        Assert.DoesNotContain(new DateTime(2026, 7, 2), result.ScheduledWorkingDates);
        Assert.DoesNotContain(new DateTime(2026, 7, 4), result.ScheduledWorkingDates);
    }

    [Fact]
    public void LegacyScheduleFallbackUsesWeekdaysAndStillExcludesConfiguredHolidays()
    {
        var calculator = new PayrollWorkdayCalculator();
        var fallbackDates = calculator.CreateLegacyWeekdaySchedule(PeriodStart, PeriodStart.AddDays(6));
        var holiday = PeriodStart.AddDays(1);

        var result = calculator.Calculate(fallbackDates, [PeriodStart, holiday, PeriodStart.AddDays(3)], [], [holiday]);

        Assert.Equal(5, fallbackDates.Count);
        Assert.DoesNotContain(PeriodStart.AddDays(3), fallbackDates);
        Assert.Equal(1m, result.ActualWorkDays);
        Assert.DoesNotContain(holiday, result.ScheduledWorkingDates);
    }

    [Fact]
    public async Task PolicyLookupReturnsNullBeforeFirstEffectiveDateAndSelectsCurrentVersion()
    {
        await using var context = CreateContext();
        var policy = CreatePolicy();
        context.PayrollPolicies.Add(policy);
        await context.SaveChangesAsync();
        var service = new PayrollPolicyService(context);

        Assert.Null(await service.GetActivePolicy(new DateTime(2026, 6, 30)));
        Assert.Equal(policy.Id, (await service.GetActivePolicy(PeriodStart))?.Id);
    }

    [Fact]
    public async Task DuplicatePolicyEffectiveDateIsRejectedWithoutChangingExistingPolicyOrBrackets()
    {
        await using var context = CreateContext();
        var existing = CreatePolicy();
        existing.EffectiveDate = PeriodStart;
        existing.SourceReference = "Original source";
        existing.TaxBrackets =
        [
            new PayrollTaxBracket { Sequence = 1, UpperLimit = 5_000_000m, Rate = 0.05m, Name = "Original tier 1" },
            new PayrollTaxBracket { Sequence = 2, UpperLimit = 10_000_000m, Rate = 0.10m, Name = "Original tier 2" },
            new PayrollTaxBracket { Sequence = 3, UpperLimit = 18_000_000m, Rate = 0.15m, Name = "Original tier 3" },
            new PayrollTaxBracket { Sequence = 4, UpperLimit = 32_000_000m, Rate = 0.20m, Name = "Original tier 4" },
            new PayrollTaxBracket { Sequence = 5, UpperLimit = 52_000_000m, Rate = 0.25m, Name = "Original tier 5" },
            new PayrollTaxBracket { Sequence = 6, UpperLimit = 80_000_000m, Rate = 0.30m, Name = "Original tier 6" },
            new PayrollTaxBracket { Sequence = 7, UpperLimit = null, Rate = 0.35m, Name = "Original tier 7" }
        ];
        context.PayrollPolicies.Add(existing);
        await context.SaveChangesAsync();
        var service = new PayrollPolicyService(context);
        var request = new SavePayrollPolicyRequest
        {
            EffectiveDate = PeriodStart,
            ReferenceInsuranceSalary = 3_000_000m,
            InsuranceCeiling = 60_000_000m,
            SourceReference = "Duplicate request",
            TaxBrackets = [new PayrollTaxBracketDto { Sequence = 1, UpperLimit = null, Rate = 0.05m, Name = "New" }]
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.CreatePolicyAsync(request, 1));

        var savedPolicy = await context.PayrollPolicies.Include(x => x.TaxBrackets).SingleAsync();
        Assert.Contains("Đã tồn tại policy", exception.Message);
        Assert.Equal("Original source", savedPolicy.SourceReference);
        Assert.Equal(7, savedPolicy.TaxBrackets.Count);
        Assert.Equal("Original tier 1", savedPolicy.TaxBrackets.OrderBy(x => x.Sequence).First().Name);
    }

    [Fact]
    public void FormulaEvaluatorResolvesDependenciesAndArithmetic()
    {
        var engine = new PayrollCalculationEngine(new PayrollFormulaEvaluator());
        var components = new[]
        {
            new PayrollComponentInput("BONUS", "Bonus", "Earning", "Formula", "RATE * BASE", "Taxable", null, "Excluded", null),
            new PayrollComponentInput("BASE", "Base", "CalculationBase", "Manual", null, "Exempt", null, "Excluded", 100m),
            new PayrollComponentInput("RATE", "Rate", "CalculationBase", "Manual", null, "Exempt", null, "Excluded", 0.15m)
        };

        var result = engine.EvaluateComponents(components);

        Assert.Equal(15m, result.Single(x => x.Definition.Code == "BONUS").Amount);
    }

    [Fact]
    public void LegalDefaultsDisplayFiveTaxBracketsFromActivePolicy()
    {
        var defaults = new SalaryConfigModel.LegalDefaultsConfig();
        var policy = CreatePolicy();
        policy.EffectiveDate = new DateTime(2026, 7, 1);
        policy.TaxBrackets =
        [
            new PayrollTaxBracket { Sequence = 1, UpperLimit = 10_000_000m, Rate = 0.05m, Name = "Tier 1" },
            new PayrollTaxBracket { Sequence = 2, UpperLimit = 30_000_000m, Rate = 0.10m, Name = "Tier 2" },
            new PayrollTaxBracket { Sequence = 3, UpperLimit = 60_000_000m, Rate = 0.20m, Name = "Tier 3" },
            new PayrollTaxBracket { Sequence = 4, UpperLimit = 100_000_000m, Rate = 0.30m, Name = "Tier 4" },
            new PayrollTaxBracket { Sequence = 5, UpperLimit = null, Rate = 0.35m, Name = "Tier 5" }
        ];

        defaults.Apply(policy);

        Assert.Equal(5, defaults.TaxBrackets.Count);
        Assert.Contains("10,000,000", defaults.TaxBrackets[0]);
        Assert.Contains("5%", defaults.TaxBrackets[0]);
        Assert.Contains("100,000,000", defaults.TaxBrackets[3]);
        Assert.Contains("35%", defaults.TaxBrackets[4]);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static async Task SeedEmployeeAndWorkdaysAsync(AppDbContext context, int attendanceDays)
    {
        context.Employees.Add(new Employee
        {
            Id = 1,
            EmployeeCode = "E001",
            FullName = "Test Employee",
            IsActive = true,
            BaseSalary = 8_000_000m,
            Allowance = 1_000_000m,
            PenaltyPerLateMinute = 0
        });
        context.Shifts.Add(new Shift { Id = 1, ShiftCode = "DAY", ShiftName = "Day", StartTime = TimeSpan.FromHours(9), EndTime = TimeSpan.FromHours(17) });
        context.EmployeeShifts.Add(new EmployeeShift { EmployeeId = 1, ShiftId = 1, EffectiveFromDate = PeriodStart });
        var weekdays = Enumerable.Range(0, 31)
            .Select(offset => PeriodStart.AddDays(offset))
            .Where(date => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            .Take(attendanceDays);
        var attendanceId = 1L;
        context.Attendances.AddRange(weekdays.Select(date => new Attendance
        {
            AttendanceId = attendanceId++,
            EmployeeId = 1,
            WorkDate = date,
            CheckInTime = date.AddHours(9),
            LateMinutes = 0
        }));
        await context.SaveChangesAsync();
    }

    private static SalaryTemplate CreateTemplate(params SalaryComponent[] components)
    {
        var template = new SalaryTemplate { Id = 10, Code = "TEST", Name = "Test template" };
        foreach (var component in components)
        {
            template.Details.Add(new SalaryTemplateDetail
            {
                SalaryTemplateId = template.Id,
                SalaryComponentId = component.Id,
                SalaryComponent = component,
                DefaultValue = null
            });
        }
        return template;
    }

    private static SalaryComponent CreateComponent(
        long id,
        string code,
        string category,
        string calculationType,
        string taxTreatment,
        string insuranceTreatment,
        decimal? taxExemptLimit = null) => new()
    {
        Id = id,
        Code = code,
        Name = code,
        Category = category,
        CalculationType = calculationType,
        TaxTreatment = taxTreatment,
        InsuranceTreatment = insuranceTreatment,
        TaxExemptLimit = taxExemptLimit
    };

    private static async Task SeedSalaryRevisionAsync(
        AppDbContext context,
        SalaryTemplate template,
        IReadOnlyDictionary<string, decimal?> amounts,
        bool isCasualIncome = false)
    {
        var components = template.Details.Select(x => x.SalaryComponent!).ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        context.SalaryComponents.AddRange(components.Values);
        context.SalaryTemplates.Add(template);
        var salary = new EmployeeSalary
        {
            Id = 1,
            EmployeeId = 1,
            SalaryTemplateId = template.Id,
            SalaryTemplate = template,
            EffectiveDate = PeriodStart,
            Status = "Active",
            IsCasualIncome = isCasualIncome,
            CreatedBy = 1
        };
        foreach (var templateDetail in template.Details)
        {
            salary.Details.Add(new EmployeeSalaryDetail
            {
                EmployeeSalaryId = salary.Id,
                SalaryComponentId = templateDetail.SalaryComponentId,
                SalaryComponent = templateDetail.SalaryComponent,
                Amount = amounts.GetValueOrDefault(templateDetail.SalaryComponent!.Code)
            });
        }
        context.EmployeeSalaries.Add(salary);
        await context.SaveChangesAsync();
    }

    private static PayrollPolicy CreatePolicy() => new()
    {
        EffectiveDate = PeriodStart,
        ReferenceInsuranceSalary = 0m,
        InsuranceCeiling = 100_000_000m,
        EmployeeSocialInsuranceRate = 0m,
        EmployeeHealthInsuranceRate = 0m,
        EmployeeUnemploymentInsuranceRate = 0m,
        EmployerSocialInsuranceRate = 0m,
        EmployerHealthInsuranceRate = 0m,
        EmployerUnemploymentInsuranceRate = 0m,
        PersonalDeduction = 0m,
        DependentDeduction = 0m,
        CasualIncomeWithholdingThreshold = 5_000_000m,
        CasualIncomeWithholdingRate = 0.1m,
        MealTaxExemptLimit = 1_200_000m,
        SourceReference = "Test policy",
        TaxBrackets = [new PayrollTaxBracket { Sequence = 1, UpperLimit = null, Rate = 0.1m, Name = "10%" }]
    };

    private static PayrollService CreatePayrollService(AppDbContext context, PayrollPolicy policy)
    {
        var policyService = new Mock<IPayrollPolicyService>();
        policyService.Setup(x => x.GetActivePolicy(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        var latePenaltyService = new Mock<ILatePenaltyService>();
        latePenaltyService.Setup(x => x.CalculatePenaltyAmountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0m);

        return new PayrollService(
            context,
            policyService.Object,
            new Mock<IFcmService>().Object,
            latePenaltyService.Object,
            NullLogger<PayrollService>.Instance,
            new PayrollCalculationEngine(new PayrollFormulaEvaluator()),
            new HybridScheduleService(context),
            new PayrollWorkdayCalculator());
    }
}
