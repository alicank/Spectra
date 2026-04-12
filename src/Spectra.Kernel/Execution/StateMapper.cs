using System.Text.RegularExpressions;
using Spectra.Contracts.State;
using Spectra.Contracts.Workflow;

namespace Spectra.Kernel.Execution;

public class StateMapper : IStateMapper
{
    // Only match {{ ... }} where the content looks like a valid state path
    // (e.g. "Inputs.repoPath", "Context.scan.files", "Artifacts.result").
    // A valid path is: optional whitespace, then one or more segments of [a-zA-Z0-9_]
    // separated by dots, then optional whitespace.
    // This prevents matching JSX expressions like {{ opacity: 0, y: -10 }}
    // or style={{ width: "100%" }} which contain spaces, colons, quotes, etc.
    private static readonly Regex TemplateRegex = new(
        @"\{\{\s*([\w]+(?:\.[\w]+)*)\s*\}\}",
        RegexOptions.Compiled);

    /// <summary>
    /// Valid state path roots for template resolution.
    /// Only {{ paths }} starting with these prefixes are treated as templates.
    /// </summary>
    private static readonly HashSet<string> ValidPathRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "Inputs", "Context", "Artifacts"
    };

    /// <inheritdoc />
    public Dictionary<string, object?> ResolveInputs(NodeDefinition node, WorkflowState state)
    {
        var inputs = new Dictionary<string, object?>();

        // 1) Start with parameters as defaults
        foreach (var param in node.Parameters)
        {
            inputs[param.Key] = NormalizeValue(param.Value);
        }

        // 2) Override with mapped values from state
        foreach (var mapping in node.InputMappings)
        {
            var value = GetValueFromPath(state, mapping.Value);
            inputs[mapping.Key] = NormalizeValue(value);
        }

        // 3) Render templates in any string inputs (including those from parameters)
        var rendered = new Dictionary<string, object?>(inputs.Count);
        foreach (var (k, v) in inputs)
        {
            rendered[k] = RenderValue(v, state);
        }

        return rendered;
    }

    /// <inheritdoc />
    public void ApplyOutputs(NodeDefinition node, WorkflowState state, Dictionary<string, object?> outputs)
    {
        foreach (var mapping in node.OutputMappings)
        {
            if (outputs.TryGetValue(mapping.Key, out var value))
            {
                SetValueAtPath(state, mapping.Value, value);
            }
        }

        // If no mappings, put all outputs in Context under node id
        if (node.OutputMappings.Count == 0 && outputs.Count > 0)
        {
            state.Context[node.Id] = outputs;
        }
    }

    /// <summary>
    /// Supports nested paths like:
    /// Inputs.repoPath
    /// Context.list.files
    /// Artifacts.GeneratedFiles
    /// </summary>
    public static object? GetValueFromPath(WorkflowState state, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        object? current = parts[0].ToLowerInvariant() switch
        {
            "inputs" => state.Inputs,
            "context" => state.Context,
            "artifacts" => state.Artifacts,
            _ => null
        };

        // If the caller gave just "Inputs"/"Context"/"Artifacts"
        if (parts.Length == 1) return current;

        for (int i = 1; i < parts.Length; i++)
        {
            if (current is null) return null;

            var seg = parts[i];

            // Most common: Dictionary<string, object?>
            if (current is IDictionary<string, object?> dict)
            {
                dict.TryGetValue(seg, out current);
                continue;
            }

            // If someone stored a non-generic dictionary
            if (current is System.Collections.IDictionary anyDict)
            {
                current = anyDict.Contains(seg) ? anyDict[seg] : null;
                continue;
            }

            // If someone stored step outputs as object and it's not a dict, we can't go deeper
            return null;
        }

        return current;
    }

    /// <summary>
    /// Supports nested SetValueAtPath (creates dictionaries as needed).
    /// Example: Context.scan.files
    /// </summary>
    public static void SetValueAtPath(WorkflowState state, string path, object? value)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        IDictionary<string, object?> rootDict = parts[0].ToLowerInvariant() switch
        {
            "inputs" => state.Inputs,
            "context" => state.Context,
            "artifacts" => state.Artifacts,
            _ => state.Context
        };

        if (parts.Length == 1)
        {
            // default set on root name if weird path
            rootDict[parts[0]] = value;
            return;
        }

        IDictionary<string, object?> current = rootDict;

        // Walk/create intermediate dictionaries
        for (int i = 1; i < parts.Length - 1; i++)
        {
            var seg = parts[i];

            if (!current.TryGetValue(seg, out var next) || next is not IDictionary<string, object?> nextDict)
            {
                nextDict = new Dictionary<string, object?>();
                current[seg] = nextDict;
            }

            current = nextDict;
        }

        // Set final
        var last = parts[^1];
        current[last] = value;
    }

    // ---------------- private helpers ----------------

    private static object? NormalizeValue(object? value)
    {
        if (value is null)
            return null;

        if (value is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => je.GetString(),
                System.Text.Json.JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Null => null,
                System.Text.Json.JsonValueKind.Object => je.EnumerateObject()
                    .ToDictionary(p => p.Name, p => NormalizeValue(p.Value)),
                System.Text.Json.JsonValueKind.Array => je.EnumerateArray()
                    .Select(e => NormalizeValue(e)).ToList(),
                _ => je.ToString()
            };
        }

        if (value is IDictionary<string, object?> dict)
            return dict.ToDictionary(k => k.Key, v => NormalizeValue(v.Value));

        if (value is IEnumerable<object?> list && value is not string)
            return list.Select(NormalizeValue).ToList();

        return value;
    }

    private static object? RenderValue(object? value, WorkflowState state)
    {
        if (value is null) return null;

        // Only render templates in strings
        if (value is string s)
            return RenderString(s, state);

        // Render inside dictionaries/lists if you ever pass structured params
        if (value is IDictionary<string, object?> dict)
        {
            var rendered = new Dictionary<string, object?>(dict.Count);
            foreach (var (k, v) in dict)
                rendered[k] = RenderValue(v, state);
            return rendered;
        }

        if (value is IEnumerable<object?> list && value is not string)
            return list.Select(v => RenderValue(v, state)).ToList();

        return value;
    }

    private static object? RenderString(string template, WorkflowState state)
    {
        // If it's exactly "{{ path }}" -> return the *raw object* (preserve type)
        var trimmed = template.Trim();
        var exact = TemplateRegex.Match(trimmed);
        if (exact.Success && exact.Value == trimmed)
        {
            var path = exact.Groups[1].Value;
            // Only resolve if the path starts with a known state root
            var root = path.Split('.')[0];
            if (ValidPathRoots.Contains(root))
                return GetValueFromPath(state, path);
            // Not a state path — return original string unchanged
            return template;
        }

        // Otherwise, replace tokens with ToString() — but only for valid state paths
        return TemplateRegex.Replace(template, m =>
        {
            var path = m.Groups[1].Value;
            var root = path.Split('.')[0];
            if (!ValidPathRoots.Contains(root))
            {
                // Not a state path — return the original matched text unchanged
                return m.Value;
            }
            var v = GetValueFromPath(state, path);
            return v?.ToString() ?? "";
        });
    }
}
