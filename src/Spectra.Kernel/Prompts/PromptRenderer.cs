using System.Text.RegularExpressions;
using Spectra.Contracts.Prompts;

namespace Spectra.Kernel.Prompts;

/// <summary>
/// Resolves <c>{{variable}}</c> placeholders in a template string against a dictionary of values.
/// Variable keys may contain word characters, dots, hyphens, and colons
/// (e.g. <c>{{inputs.request}}</c>, <c>{{nodes.my-context.var}}</c>).
/// Optional whitespace inside the braces is tolerated: <c>{{ name }}</c> is equivalent to <c>{{name}}</c>.
/// Stateless — safe to use as a singleton.
/// </summary>
public sealed partial class PromptRenderer
{
    [GeneratedRegex(@"\{\{\s*([\w][\w.\-:]*)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex VariablePattern();

    /// <summary>
    /// Renders a template by substituting <c>{{key}}</c> placeholders with values from
    /// <paramref name="variables"/>. Behavior for missing keys is controlled by
    /// <paramref name="options"/>.
    /// </summary>
    public string Render(
        string template,
        IReadOnlyDictionary<string, object?> variables,
        PromptRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(variables);

        var behavior = (options ?? PromptRenderOptions.Default).MissingVariableBehavior;

        return VariablePattern().Replace(template, match =>
        {
            var path = match.Groups[1].Value;

            if (TryResolvePath(path, variables, out var value))
                return value?.ToString() ?? string.Empty;

            return behavior switch
            {
                MissingVariableBehavior.ThrowException =>
                    throw new KeyNotFoundException(
                        $"Prompt variable '{path}' was not found in the supplied dictionary."),

                MissingVariableBehavior.ReplaceWithEmpty => string.Empty,

                // MissingVariableBehavior.LeaveTemplate
                _ => match.Value
            };
        });
    }

    private static bool TryResolvePath(
        string path,
        IReadOnlyDictionary<string, object?> variables,
        out object? result)
    {
        result = null;
        var segments = path.Split('.');

        if (!variables.TryGetValue(segments[0], out var current))
            return false;

        if (segments.Length == 1)
        {
            result = current;
            return true;
        }

        for (var i = 1; i < segments.Length; i++)
        {
            if (current is null)
                return false;

            if (current is IDictionary<string, object?> dict)
            {
                if (!dict.TryGetValue(segments[i], out current))
                    return false;
            }
            else if (current is IDictionary<string, object> dict2)
            {
                if (!dict2.TryGetValue(segments[i], out var next))
                    return false;
                current = next;
            }
            else
            {
                return false;
            }
        }

        result = current;
        return true;
    }
}
