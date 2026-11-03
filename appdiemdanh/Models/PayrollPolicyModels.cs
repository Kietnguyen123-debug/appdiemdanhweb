namespace AppDiemDanh.Models;

public sealed class PayrollPolicy
{
    public long Id { get; set; }
    public DateTime EffectiveDate { get; set; }
    public decimal ReferenceInsuranceSalary { get; set; }
    public decimal InsuranceCeiling { get; set; }
    public decimal EmployeeSocialInsuranceRate { get; set; }
    public decimal EmployeeHealthInsuranceRate { get; set; }
    public decimal EmployeeUnemploymentInsuranceRate { get; set; }
    public decimal EmployerSocialInsuranceRate { get; set; }
    public decimal EmployerHealthInsuranceRate { get; set; }
    public decimal EmployerUnemploymentInsuranceRate { get; set; }
    public decimal PersonalDeduction { get; set; }
    public decimal DependentDeduction { get; set; }
    public decimal CasualIncomeWithholdingThreshold { get; set; }
    public decimal CasualIncomeWithholdingRate { get; set; }
    public decimal MealTaxExemptLimit { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public string? ChangeReason { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PayrollTaxBracket> TaxBrackets { get; set; } = new List<PayrollTaxBracket>();
}

public sealed class PayrollTaxBracket
{
    public long Id { get; set; }
    public long PayrollPolicyId { get; set; }
    public int Sequence { get; set; }
    public decimal? UpperLimit { get; set; }
    public decimal Rate { get; set; }
    public string Name { get; set; } = string.Empty;

    public PayrollPolicy? PayrollPolicy { get; set; }
}
