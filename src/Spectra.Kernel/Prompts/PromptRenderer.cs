using System.Text.RegularExpressions;
using Spectra.Contracts.Prompts;

namespace Spectra.Kernel.Prompts;

/// <summary>
/// Resolves <c>{{variable}}</c> placeholders in a template string against a dictionary of values.
/// Stateless — safe to use as a singleton.
/// </summary>
public sealed partial class PromptRenderer
{
    [GeneratedRegex(@"\{\{(\w+)\}\}", RegexOptions.Compiled)]
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
            var key = match.Groups[1].Value;

            if (variables.TryGetValue(key, out var value))
                return value?.ToString() ?? string.Empty;

            return behavior switch
            {
                MissingVariableBehavior.ThrowException =>
                    throw new KeyNotFoundException(
                        $"Prompt variable '{key}' was not found in the supplied dictionary."),

                MissingVariableBehavior.ReplaceWithEmpty => string.Empty,

                // MissingVariableBehavior.LeaveTemplate
                _ => match.Value
            };
        });
    }
}