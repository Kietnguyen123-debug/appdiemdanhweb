using System.ComponentModel.DataAnnotations;

namespace AppDiemDanh.ViewModels;

public sealed class EmployeeSalaryIndexViewModel
{
    public IReadOnlyList<EmployeeSalaryRowViewModel> Employees { get; init; } = [];
    public IReadOnlyList<DepartmentFilterViewModel> Departments { get; init; } = [];
    public IReadOnlyList<SalaryTemplateFilterViewModel> Templates { get; init; } = [];
}

public sealed class EmployeeSalaryRowViewModel
{
    public int Id { get; init; }
    public string EmployeeCode { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = string.Empty;
    public string BranchName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public long? SalaryId { get; init; }
    public long? SalaryTemplateId { get; init; }
    public bool IsCasualIncome { get; init; }
    public string? SalaryTemplateName { get; init; }

    // Bổ sung hiển thị thông tin thanh toán trên bảng danh sách
    public string PaymentMethod { get; init; } = "BankTransfer";
    public string? BankName { get; init; }
    public string? BankAccountNo { get; init; }
    public string? BankAccountHolder { get; init; }
}

public sealed class DepartmentFilterViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class SalaryTemplateFilterViewModel
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class BulkAssignSalaryRequest
{
    [MinLength(1)]
    public List<int> EmployeeIds { get; set; } = [];

    [Range(1, long.MaxValue)]
    public long SalaryTemplateId { get; set; }

    public DateTime EffectiveDate { get; set; }
}

public sealed class EmployeeSalaryDrawerViewModel
{
    public int EmployeeId { get; init; }
    public string EmployeeCode { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public long? SalaryId { get; init; }
    public long? SalaryTemplateId { get; init; }
    public bool IsCasualIncome { get; init; }

    // Bổ sung thông tin thanh toán khi mở Drawer xem/sửa
    public string PaymentMethod { get; init; } = "BankTransfer";
    public string? BankName { get; init; }
    public string? BankAccountNo { get; init; }
    public string? BankAccountHolder { get; init; }

    public IReadOnlyList<SalaryTemplateFilterViewModel> Templates { get; init; } = [];
    public IReadOnlyList<EmployeeSalaryComponentViewModel> Components { get; init; } = [];
}

public sealed class EmployeeSalaryComponentViewModel
{
    public long ComponentId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public string? Formula { get; init; }
}

public sealed class SaveEmployeeSalaryRequest
{
    [Range(1, int.MaxValue)]
    public int EmployeeId { get; set; }

    [Range(1, long.MaxValue)]
    public long SalaryTemplateId { get; set; }

    public DateTime EffectiveDate { get; set; }
    public bool IsCasualIncome { get; set; }

    // Bổ sung thông tin thanh toán khi gửi Form lưu từ Drawer
    public string PaymentMethod { get; set; } = "BankTransfer";
    public string? BankName { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankAccountHolder { get; set; }

    public List<EmployeeSalaryComponentInput> Components { get; set; } = [];
}

public sealed class EmployeeSalaryComponentInput
{
    [Range(1, long.MaxValue)]
    public long ComponentId { get; set; }

    public decimal? Amount { get; set; }

    [StringLength(2000)]
    public string? Formula { get; set; }
}