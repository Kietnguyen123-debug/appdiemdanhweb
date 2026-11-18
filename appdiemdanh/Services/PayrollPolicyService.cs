using AppDiemDanh.DTOs;
using AppDiemDanh.Models;
using Microsoft.EntityFrameworkCore;

namespace AppDiemDanh.Services;

public interface IPayrollPolicyService
{
    Task<PayrollPolicy?> GetActivePolicy(DateTime payPeriodDate, CancellationToken cancellationToken = default);
    Task<PayrollPolicy> CreatePolicyAsync(SavePayrollPolicyRequest request, int createdBy, CancellationToken cancellationToken = default);
}

public sealed class PayrollPolicyService : IPayrollPolicyService
{
    private readonly AppDbContext _context;

    public PayrollPolicyService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PayrollPolicy?> GetActivePolicy(DateTime payPeriodDate, CancellationToken cancellationToken = default)
    {
        var policy = await _context.PayrollPolicies
            .AsNoTracking()
            .Include(x => x.TaxBrackets)
            .Where(x => x.EffectiveDate <= payPeriodDate.Date)
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return policy;
    }

    public async Task<PayrollPolicy> CreatePolicyAsync(
        SavePayrollPolicyRequest request,
        int createdBy,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var effectiveDate = request.EffectiveDate.Date;
        if (await _context.PayrollPolicies.AnyAsync(x => x.EffectiveDate == effectiveDate, cancellationToken))
            throw new ArgumentException($"Đã tồn tại policy có ngày hiệu lực {effectiveDate:dd/MM/yyyy}.");

        var policy = new PayrollPolicy
        {
            EffectiveDate = effectiveDate,
            ReferenceInsuranceSalary = request.ReferenceInsuranceSalary,
            InsuranceCeiling = request.InsuranceCeiling,
            EmployeeSocialInsuranceRate = request.EmployeeSocialInsuranceRate,
            EmployeeHealthInsuranceRate = request.EmployeeHealthInsuranceRate,
            EmployeeUnemploymentInsuranceRate = request.EmployeeUnemploymentInsuranceRate,
            EmployerSocialInsuranceRate = request.EmployerSocialInsuranceRate,
            EmployerHealthInsuranceRate = request.EmployerHealthInsuranceRate,
            EmployerUnemploymentInsuranceRate = request.EmployerUnemploymentInsuranceRate,
            PersonalDeduction = request.PersonalDeduction,
            DependentDeduction = request.DependentDeduction,
            CasualIncomeWithholdingThreshold = request.CasualIncomeWithholdingThreshold,
            CasualIncomeWithholdingRate = request.CasualIncomeWithholdingRate,
            MealTaxExemptLimit = request.MealTaxExemptLimit,
            SourceReference = request.SourceReference.Trim(),
            ChangeReason = request.ChangeReason?.Trim(),
            CreatedBy = createdBy,
            TaxBrackets = request.TaxBrackets.OrderBy(x => x.Sequence).Select(x => new PayrollTaxBracket
            {
                Sequence = x.Sequence,
                UpperLimit = x.UpperLimit,
                Rate = x.Rate,
                Name = x.Name.Trim()
            }).ToList()
        };

        _context.PayrollPolicies.Add(policy);
        await _context.SaveChangesAsync(cancellationToken);
        return policy;
    }

    private static void ValidateRequest(SavePayrollPolicyRequest request)
    {
        if (request.EffectiveDate == default)
            throw new ArgumentException("Ngày hiệu lực là bắt buộc.");
        if (request.ReferenceInsuranceSalary <= 0 || request.InsuranceCeiling < request.ReferenceInsuranceSalary)
            throw new ArgumentException("Mức tham chiếu và trần bảo hiểm không hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.SourceReference))
            throw new ArgumentException("Văn bản nguồn là bắt buộc.");
        if (request.TaxBrackets.Count == 0)
            throw new ArgumentException("Phải có ít nhất một bậc thuế.");

        var brackets = request.TaxBrackets.OrderBy(x => x.Sequence).ToList();
        if (brackets.Select(x => x.Sequence).Distinct().Count() != brackets.Count || brackets.Any(x => x.Sequence < 1 || x.Rate < 0 || x.Rate > 1 || string.IsNullOrWhiteSpace(x.Name)))
            throw new ArgumentException("Danh sách bậc thuế không hợp lệ.");
        if (brackets.Count(x => x.UpperLimit == null) != 1 || brackets[^1].UpperLimit != null)
            throw new ArgumentException("Bậc thuế cuối cùng phải có UpperLimit = null.");
        if (brackets.Zip(brackets.Skip(1), (left, right) => left.UpperLimit is null || left.UpperLimit >= right.UpperLimit).Any(x => x))
            throw new ArgumentException("Giới hạn các bậc thuế phải tăng dần.");
    }
}
