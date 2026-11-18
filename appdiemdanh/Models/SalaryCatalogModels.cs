namespace AppDiemDanh.Models;

public sealed class SalaryComponent
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string CalculationType { get; set; } = string.Empty;
    public string? Formula { get; set; }
    public string TaxTreatment { get; set; } = string.Empty;
    public decimal? TaxExemptLimit { get; set; }
    public string InsuranceTreatment { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<SalaryTemplateDetail> TemplateDetails { get; set; } = new List<SalaryTemplateDetail>();
}

public sealed class SalaryTemplate
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? PositionGroup { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SalaryTemplateDetail> Details { get; set; } = new List<SalaryTemplateDetail>();
}

public sealed class SalaryTemplateDetail
{
    public long Id { get; set; }
    public long SalaryTemplateId { get; set; }
    public long SalaryComponentId { get; set; }
    public decimal? DefaultValue { get; set; }
    public string? Formula { get; set; }

    public SalaryTemplate? SalaryTemplate { get; set; }
    public SalaryComponent? SalaryComponent { get; set; }
}

public sealed class EmployeeSalary
{
    public long Id { get; set; }
    public int EmployeeId { get; set; }
    public long? SalaryTemplateId { get; set; }
    public DateTime EffectiveDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsCasualIncome { get; set; }
    public string? Reason { get; set; }
    public int CreatedBy { get; set; }
    public int? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Employee? Employee { get; set; }
    public SalaryTemplate? SalaryTemplate { get; set; }
    public ICollection<EmployeeSalaryDetail> Details { get; set; } = new List<EmployeeSalaryDetail>();
}

public sealed class EmployeeSalaryDetail
{
    public long Id { get; set; }
    public long EmployeeSalaryId { get; set; }
    public long SalaryComponentId { get; set; }
    public decimal? Amount { get; set; }
    public string? Formula { get; set; }

    public EmployeeSalary? EmployeeSalary { get; set; }
    public SalaryComponent? SalaryComponent { get; set; }
}
