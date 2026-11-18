using AppDiemDanh.DTOs;

namespace AppDiemDanh.Services;

public sealed record PayrollComponentInput(
    string Code,
    string Name,
    string Category,
    string CalculationType,
    string? Formula,
    string TaxTreatment,
    decimal? TaxExemptLimit,
    string InsuranceTreatment,
    decimal? Amount);

public sealed record EvaluatedPayrollComponent(PayrollComponentInput Definition, decimal Amount);
public sealed record PayrollTaxBracketInput(decimal? UpperLimit, decimal Rate, string Name);
public sealed record PayrollTaxCalculation(decimal Tax, IReadOnlyList<ProgressiveTaxBracketDto> Brackets);

public sealed class PayrollCalculationEngine
{
    private readonly PayrollFormulaEvaluator _formulaEvaluator;

    public PayrollCalculationEngine(PayrollFormulaEvaluator formulaEvaluator)
    {
        _formulaEvaluator = formulaEvaluator;
    }

    public IReadOnlyList<EvaluatedPayrollComponent> EvaluateComponents(
        IEnumerable<PayrollComponentInput> components,
        IReadOnlyDictionary<string, decimal>? payrollVariables = null)
    {
        ArgumentNullException.ThrowIfNull(components);

        var componentList = components.ToList();
        if (componentList.Any(x => string.IsNullOrWhiteSpace(x.Code)))
            throw new ArgumentException("Every payroll component must have a code.", nameof(components));
        if (componentList.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != componentList.Count)
            throw new ArgumentException("Payroll component codes must be unique.", nameof(components));

        var values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (payrollVariables != null)
        {
            foreach (var variable in payrollVariables)
                values[variable.Key] = variable.Value;
        }

        var calculated = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<PayrollComponentInput>();
        foreach (var component in componentList)
        {
            var formula = string.IsNullOrWhiteSpace(component.Formula) ? null : component.Formula;
            if (string.Equals(component.CalculationType, "Formula", StringComparison.OrdinalIgnoreCase) && formula != null)
                pending.Add(component);
            else
            {
                var amount = component.Amount ?? 0m;
                calculated.Add(component.Code, amount);
                values[component.Code] = amount;
            }
        }

        while (pending.Count > 0)
        {
            var calculatedInPass = false;
            for (var index = pending.Count - 1; index >= 0; index--)
            {
                var component = pending[index];
                try
                {
                    var amount = _formulaEvaluator.Evaluate(component.Formula!, values);
                    calculated.Add(component.Code, amount);
                    values[component.Code] = amount;
                    pending.RemoveAt(index);
                    calculatedInPass = true;
                }
                catch (KeyNotFoundException)
                {
                }
            }

            if (!calculatedInPass)
            {
                var unresolved = string.Join(", ", pending.Select(x => x.Code));
                throw new InvalidOperationException($"Could not resolve payroll formulas for components: {unresolved}.");
            }
        }

        return componentList.Select(component => new EvaluatedPayrollComponent(component, calculated[component.Code])).ToList();
    }

    public decimal CalculateTaxableAmount(EvaluatedPayrollComponent component, decimal amount, decimal mealTaxExemptLimit)
    {
        return component.Definition.TaxTreatment.Trim().ToUpperInvariant() switch
        {
            "TAXABLE" => amount,
            "EXEMPT" => 0m,
            "CAPPEDEXEMPT" => Math.Max(0m, amount - GetExemptionLimit(component.Definition, mealTaxExemptLimit)),
            _ => throw new InvalidOperationException($"Unsupported tax treatment '{component.Definition.TaxTreatment}' for component '{component.Definition.Code}'.")
        };
    }

    public PayrollTaxCalculation CalculateProgressiveTax(decimal taxableIncome, IEnumerable<PayrollTaxBracketInput> policyBrackets)
    {
        var result = new List<ProgressiveTaxBracketDto>();
        decimal previous = 0;
        decimal total = 0;
        foreach (var bracket in policyBrackets)
        {
            var upperLimit = bracket.UpperLimit ?? decimal.MaxValue;
            var taxableAmount = Math.Max(0m, Math.Min(taxableIncome, upperLimit) - previous);
            if (taxableAmount > 0)
            {
                var tax = Math.Round(taxableAmount * bracket.Rate, 0);
                result.Add(new ProgressiveTaxBracketDto
                {
                    Bracket = bracket.Name,
                    TaxableAmount = taxableAmount,
                    Rate = bracket.Rate,
                    TaxAmount = tax
                });
                total += tax;
            }
            if (taxableIncome <= upperLimit) break;
            previous = upperLimit;
        }

        return new PayrollTaxCalculation(total, result);
    }

    public decimal CalculateCasualWithholding(decimal taxableGross, decimal threshold, decimal rate) =>
        taxableGross >= threshold ? Math.Round(taxableGross * rate, 0) : 0m;

    private static decimal GetExemptionLimit(PayrollComponentInput component, decimal mealTaxExemptLimit) =>
        string.Equals(component.Code, "ALLOW_MEAL", StringComparison.OrdinalIgnoreCase)
            ? mealTaxExemptLimit
            : component.TaxExemptLimit ?? 0m;
}
