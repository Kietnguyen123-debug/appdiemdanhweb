using System.ComponentModel.DataAnnotations;

namespace AppDiemDanh.ViewModels;

public sealed class SalaryTemplateIndexViewModel
{
    public IReadOnlyList<SalaryTemplateListItemViewModel> Templates { get; init; } = [];
}

public sealed class SalaryTemplateListItemViewModel
{
    public long Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? PositionGroup { get; init; }
    public int ComponentCount { get; init; }
}

public sealed class CreateSalaryTemplateViewModel
{
    [Required, StringLength(80)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(120)]
    public string? PositionGroup { get; set; }
}

public sealed class SalaryTemplateDetailViewModel
{
    public long Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? PositionGroup { get; init; }
    public IReadOnlyList<SalaryTemplateComponentRowViewModel> Components { get; init; } = [];
}

public sealed class SalaryTemplateComponentRowViewModel
{
    public long ComponentId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string CalculationType { get; init; } = string.Empty;
    public bool Selected { get; init; }
    public decimal? DefaultValue { get; init; }
    public string? Formula { get; init; }
}

public sealed class SaveSalaryTemplateComponentsRequest
{
    public List<SalaryTemplateComponentInput> Components { get; set; } = [];
}

public sealed class SalaryTemplateComponentInput
{
    [Range(1, long.MaxValue)]
    public long ComponentId { get; set; }

    public bool Selected { get; set; }

    public decimal? DefaultValue { get; set; }

    [StringLength(2000)]
    public string? Formula { get; set; }
}
