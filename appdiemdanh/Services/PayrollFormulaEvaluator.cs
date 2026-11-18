using System.Globalization;

namespace AppDiemDanh.Services;

public sealed class PayrollFormulaEvaluator
{
    public decimal Evaluate(string formula, IReadOnlyDictionary<string, decimal> variables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formula);
        ArgumentNullException.ThrowIfNull(variables);

        var parser = new FormulaParser(formula, variables);
        return parser.Parse();
    }

    private sealed class FormulaParser
    {
        private readonly string _formula;
        private readonly IReadOnlyDictionary<string, decimal> _variables;
        private int _position;

        public FormulaParser(string formula, IReadOnlyDictionary<string, decimal> variables)
        {
            _formula = formula;
            _variables = variables;
        }

        public decimal Parse()
        {
            var value = ParseExpression();
            SkipWhitespace();
            if (_position != _formula.Length)
                throw new FormatException($"Unexpected character at position {_position} in payroll formula.");
            return value;
        }

        private decimal ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume('+')) value += ParseTerm();
                else if (TryConsume('-')) value -= ParseTerm();
                else return value;
            }
        }

        private decimal ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume('*')) value *= ParseFactor();
                else if (TryConsume('/'))
                {
                    var divisor = ParseFactor();
                    if (divisor == 0) throw new DivideByZeroException("Payroll formula cannot divide by zero.");
                    value /= divisor;
                }
                else return value;
            }
        }

        private decimal ParseFactor()
        {
            SkipWhitespace();
            if (TryConsume('+')) return ParseFactor();
            if (TryConsume('-')) return -ParseFactor();
            if (TryConsume('('))
            {
                var value = ParseExpression();
                SkipWhitespace();
                if (!TryConsume(')')) throw new FormatException("Missing closing parenthesis in payroll formula.");
                return value;
            }

            if (_position >= _formula.Length)
                throw new FormatException("Unexpected end of payroll formula.");

            if (char.IsDigit(_formula[_position]) || _formula[_position] == '.')
                return ParseNumber();

            if (char.IsLetter(_formula[_position]) || _formula[_position] == '_')
                return ParseVariable();

            throw new FormatException($"Unexpected character at position {_position} in payroll formula.");
        }

        private decimal ParseNumber()
        {
            var start = _position;
            var decimalSeparatorSeen = false;
            while (_position < _formula.Length)
            {
                var current = _formula[_position];
                if (char.IsDigit(current))
                {
                    _position++;
                    continue;
                }
                if (current == '.' && !decimalSeparatorSeen)
                {
                    decimalSeparatorSeen = true;
                    _position++;
                    continue;
                }
                break;
            }

            if (!decimal.TryParse(_formula.AsSpan(start, _position - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"Invalid number in payroll formula at position {start}.");
            return value;
        }

        private decimal ParseVariable()
        {
            var start = _position++;
            while (_position < _formula.Length && (char.IsLetterOrDigit(_formula[_position]) || _formula[_position] == '_'))
                _position++;

            var name = _formula[start.._position];
            foreach (var variable in _variables)
            {
                if (string.Equals(variable.Key, name, StringComparison.OrdinalIgnoreCase))
                    return variable.Value;
            }

            throw new KeyNotFoundException($"Payroll formula references unknown variable '{name}'.");
        }

        private void SkipWhitespace()
        {
            while (_position < _formula.Length && char.IsWhiteSpace(_formula[_position]))
                _position++;
        }

        private bool TryConsume(char value)
        {
            if (_position >= _formula.Length || _formula[_position] != value)
                return false;
            _position++;
            return true;
        }
    }
}
