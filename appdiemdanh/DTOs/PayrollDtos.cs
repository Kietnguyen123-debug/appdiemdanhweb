using System.ComponentModel.DataAnnotations;
using AppDiemDanh.Models;

namespace AppDiemDanh.DTOs
{
    public class PayslipResponse
    {
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int Month { get; set; }
        public int Year { get; set; }
        public decimal BaseSalary { get; set; }
        public decimal Allowance { get; set; }
        public decimal StandardWorkDays { get; set; }
        public decimal ActualWorkDays { get; set; }
        public int TotalLateMinutes { get; set; }
        public decimal TotalPenalty { get; set; }
        public decimal GrossSalary { get; set; }
        public decimal NetSalary { get; set; }
        public decimal OvertimeSalary { get; set; }
        public decimal OvertimeTaxExemptAmount { get; set; }
        public decimal InsuranceBase { get; set; }
        public decimal InsuranceDeduction { get; set; }
        public decimal EmployerInsuranceAmount { get; set; }
        public decimal PaidLeaveDays { get; set; }
        public decimal TotalInsuranceDays { get; set; }
        public bool SubjectToInsurance { get; set; }
        public decimal TaxableIncome { get; set; }
        public bool IsCasualIncome { get; set; }
        public decimal PersonalDeduction { get; set; }
        public decimal DependentDeduction { get; set; }
        public int DependentCount { get; set; }
        public decimal Pit { get; set; }
        public decimal ComponentDeductions { get; set; }
        public decimal SalaryAdvanceDeduction { get; set; }
        public IReadOnlyList<ProgressiveTaxBracketDto> TaxBrackets { get; set; } = Array.Empty<ProgressiveTaxBracketDto>();
        public DateTime PolicyEffectiveDate { get; set; }

        // Bổ sung thông tin thanh toán cho Bảng lương & Excel Export
        public string PaymentMethod { get; set; } = "BankTransfer";
        public string? BankName { get; set; }
        public string? BankAccountNo { get; set; }
        public string? BankAccountHolder { get; set; }
    }

    public sealed class PayrollPolicyResponse
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
        public IReadOnlyList<PayrollTaxBracketDto> TaxBrackets { get; set; } = Array.Empty<PayrollTaxBracketDto>();
    }

    public sealed class PayrollTaxBracketDto
    {
        public int Sequence { get; set; }
        public decimal? UpperLimit { get; set; }
        public decimal Rate { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class SavePayrollPolicyRequest
    {
        [Required] public DateTime EffectiveDate { get; set; }
        [Range(typeof(decimal), "0", "1000000000000")] public decimal ReferenceInsuranceSalary { get; set; }
        [Range(typeof(decimal), "0", "1000000000000")] public decimal InsuranceCeiling { get; set; }
        [Range(typeof(decimal), "0", "1")] public decimal EmployeeSocialInsuranceRate { get; set; }
        [Range(typeof(decimal), "0", "1")] public decimal EmployeeHealthInsuranceRate { get; set; }
        [Range(typeof(decimal), "0", "1")] public decimal EmployeeUnemploymentInsuranceRate { get; set; }
        [Range(typeof(decimal), "0", "1")] public decimal EmployerSocialInsuranceRate { get; set; }
        [Range(typeof(decimal), "0", "1")] public decimal EmployerHealthInsuranceRate { get; set; }
        [Range(typeof(decimal), "0", "1")] public decimal EmployerUnemploymentInsuranceRate { get; set; }
        [Range(typeof(decimal), "0", "1000000000000")] public decimal PersonalDeduction { get; set; }
        [Range(typeof(decimal), "0", "1000000000000")] public decimal DependentDeduction { get; set; }
        [Range(typeof(decimal), "0", "1000000000000")] public decimal CasualIncomeWithholdingThreshold { get; set; }
        [Range(typeof(decimal), "0", "1")] public decimal CasualIncomeWithholdingRate { get; set; }
        [Range(typeof(decimal), "0", "1000000000000")] public decimal MealTaxExemptLimit { get; set; }
        [Required, StringLength(500)] public string SourceReference { get; set; } = string.Empty;
        [StringLength(1000)] public string? ChangeReason { get; set; }
        [Required, MinLength(1)] public List<PayrollTaxBracketDto> TaxBrackets { get; set; } = [];
    }

    public class ProgressiveTaxBracketDto
    {
        public string Bracket { get; set; } = string.Empty;
        public decimal TaxableAmount { get; set; }
        public decimal Rate { get; set; }
        public decimal TaxAmount { get; set; }
    }

    public class SalaryAdvanceRequest
    {
        [Range(typeof(decimal), "0.01", "1000000000000")]
        public decimal Amount { get; set; }
        [Required, StringLength(1000, MinimumLength = 1)]
        public string Reason { get; set; } = string.Empty;
    }

    public class SalaryAdvanceReviewRequest
    {
        public bool IsApproved { get; set; }
        public string? ReviewNote { get; set; }
    }

    public class OvertimeRequest
    {
        [Required]
        public DateTime WorkDate { get; set; }
        [Range(0.01, 24)]
        public double Hours { get; set; }
        [Range(0.01, 10)]
        public double Multiplier { get; set; } = 1.5;
    }

    public class OvertimeReviewRequest
    {
        public bool IsApproved { get; set; }
    }

    public class UpdateSalaryConfigRequest
    {
        [Range(typeof(decimal), "0", "1000000000000")]
        public decimal BaseSalary { get; set; }
        [Range(typeof(decimal), "0", "1000000000000")]
        public decimal Allowance { get; set; }
        [Range(typeof(decimal), "0", "1000000000")]
        public decimal PenaltyPerLateMinute { get; set; }
        [Range(0, 20)]
        public int DependentCount { get; set; }
        public double? SocialInsuranceBase { get; set; }
        [Required]
        public DateTime EffectiveFrom { get; set; } = DateTime.Today;

        // Bổ sung thông tin thanh toán
        public string PaymentMethod { get; set; } = "BankTransfer";
        public string? BankName { get; set; }
        public string? BankAccountNo { get; set; }
        public string? BankAccountHolder { get; set; }
    }

    public class LatePenaltyRangeRequest
    {
        [Range(0, int.MaxValue)]
        public int FromMinute { get; set; }
        [Range(0, int.MaxValue)]
        public int ToMinute { get; set; }
        [Range(typeof(decimal), "0", "1000000000000")]
        public decimal PenaltyAmount { get; set; }
    }

    public class LatePenaltyPolicyRequest
    {
        [Required, StringLength(200, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;
        [EnumDataType(typeof(PenaltyType))]
        public PenaltyType Type { get; set; }
        [Range(typeof(decimal), "0", "1000000000000")]
        public decimal? AmountPerMinute { get; set; }
        public bool IsActive { get; set; } = true;
        public List<LatePenaltyRangeRequest> Ranges { get; set; } = [];
    }

    public class LatePenaltyPolicyResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public PenaltyType Type { get; set; }
        public decimal? AmountPerMinute { get; set; }
        public bool IsActive { get; set; }
        public IReadOnlyList<LatePenaltyRangeRequest> Ranges { get; set; } = [];
    }
}