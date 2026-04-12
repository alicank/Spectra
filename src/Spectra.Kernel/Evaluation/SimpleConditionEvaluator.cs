using System.Text.RegularExpressions;
using Spectra.Contracts.Evaluation;
using Spectra.Contracts.State;

namespace Spectra.Kernel.Evaluation;

public partial class SimpleConditionEvaluator : IConditionEvaluator
{
    public ConditionResult Evaluate(string expression, WorkflowState state)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return ConditionResult.True("Empty condition always passes");
        }

        try
        {
            var match = ConditionRegex().Match(expression.Trim());

            if (!match.Success)
            {
                var value = GetValue(expression.Trim(), state);
                var isTruthy = IsTruthy(value);

                return isTruthy
                    ? ConditionResult.True($"{expression} is truthy")
                    : ConditionResult.False($"{expression} is falsy");
            }

            var path = match.Groups["path"].Value;
            var op = match.Groups["op"].Value;
            var comparand = match.Groups["value"].Value;

            var actualValue = GetValue(path, state);
            var result = Compare(actualValue, op, comparand);

            return result
                ? ConditionResult.True($"{path} {op} {comparand}")
                : ConditionResult.False($"{path} ({actualValue}) {op} {comparand} is false");
        }
        catch (Exception ex)
        {
            return ConditionResult.False($"Evaluation error: {ex.Message}");
        }
    }

    private static object? GetValue(string path, WorkflowState state)
    {
        var parts = path.Split('.');
        if (parts.Length == 0)
        {
            return null;
        }

        var root = parts[0] switch
        {
            "Inputs" => state.Inputs as object,
            "Context" => state.Context,
            "Artifacts" => state.Artifacts,
            "Errors" => state.Errors,
            "RunId" => state.RunId,
            "CurrentNodeId" => state.CurrentNodeId,
            _ => state.Context.GetValueOrDefault(parts[0])
        };

        if (root == null || parts.Length == 1)
        {
            return root;
        }

        object? current = root;
        for (var i = 1; i < parts.Length; i++)
        {
            current = GetNestedValue(current, parts[i]);
            if (current == null)
            {
                break;
            }
        }

        return current;
    }

    private static object? GetNestedValue(object? obj, string key)
    {
        return obj switch
        {
            Dictionary<string, object?> dict => dict.GetValueOrDefault(key),
            IDictionary<string, object> dict => dict.TryGetValue(key, out var value) ? value : null,
            _ => null
        };
    }

    private static bool Compare(object? actualValue, string op, string comparand)
    {
        if (actualValue == null)
        {
            return op switch
            {
                "==" => comparand.Equals("null", StringComparison.OrdinalIgnoreCase),
                "!=" => !comparand.Equals("null", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        if (bool.TryParse(comparand, out var boolComparand))
        {
            var actualBool = actualValue switch
            {
                bool b => b,
                string s => bool.TryParse(s, out var parsed) && parsed,
                int i => i != 0,
                _ => false
            };

            return op switch
            {
                "==" => actualBool == boolComparand,
                "!=" => actualBool != boolComparand,
                _ => false
            };
        }

        if (double.TryParse(comparand, out var numComparand) &&
            TryGetNumber(actualValue, out var actualNum))
        {
            return op switch
            {
                "==" => Math.Abs(actualNum - numComparand) < 0.0001,
                "!=" => Math.Abs(actualNum - numComparand) >= 0.0001,
                ">" => actualNum > numComparand,
                ">=" => actualNum >= numComparand,
                "<" => actualNum < numComparand,
                "<=" => actualNum <= numComparand,
                _ => false
            };
        }

        var stringComparand = comparand.Trim('"', '\'');
        var actualString = actualValue.ToString() ?? string.Empty;

        return op switch
        {
            "==" => actualString.Equals(stringComparand, StringComparison.OrdinalIgnoreCase),
            "!=" => !actualString.Equals(stringComparand, StringComparison.OrdinalIgnoreCase),
            "contains" => actualString.Contains(stringComparand, StringComparison.OrdinalIgnoreCase),
            "startswith" => actualString.StartsWith(stringComparand, StringComparison.OrdinalIgnoreCase),
            "endswith" => actualString.EndsWith(stringComparand, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool TryGetNumber(object? value, out double result)
    {
        result = 0;

        return value switch
        {
            int i => (result = i) == i,
            long l => (result = l) == l,
            double d => (result = d) == d,
            float f => (result = f) == f,
            string s => double.TryParse(s, out result),
            _ => false
        };
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            double d => d != 0,
            string s => !string.IsNullOrEmpty(s) &&
                        !s.Equals("false", StringComparison.OrdinalIgnoreCase),
            ICollection<object> c => c.Count > 0,
            IEnumerable<object> e => e.Any(),
            _ => true
        };
    }

    [GeneratedRegex(
        @"^(?<path>[\w.]+)\s*(?<op>==|!=|>=|<=|>|<|contains|startswith|endswith)\s*(?<value>.+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ConditionRegex();
}