namespace Spectra.Kernel.Prompts;

/// <summary>
/// Minimal YAML-like front-matter parser. Handles:
/// <list type="bullet">
///   <item>Scalar values: <c>key: value</c></item>
///   <item>Quoted scalars: <c>key: "value"</c> or <c>key: 'value'</c></item>
///   <item>String lists: <c>  - item</c> lines following a <c>key:</c> line</item>
/// </list>
/// All other keys are placed in a metadata dictionary.
/// </summary>
internal static class FrontMatterParser
{
    private const string Fence = "---";

    /// <summary>
    /// Splits raw file text into front-matter key/value pairs and the body content.
    /// Returns <c>null</c> metadata when no front-matter is present.
    /// </summary>
    public static (Dictionary<string, object>? Meta, string Body) Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return (null, raw ?? string.Empty);

        var lines = raw.Split('\n');

        // First non-blank line must be the opening fence.
        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            if (lines[i].Trim() == Fence) { start = i; break; }
            return (null, raw); // content before fence → no front-matter
        }

        if (start < 0) return (null, raw);

        // Find closing fence.
        var end = -1;
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == Fence) { end = i; break; }
        }

        if (end < 0) return (null, raw); // unclosed → treat entire text as body

        var meta = ParseKeyValues(lines.AsSpan()[(start + 1)..end]);

        // Body is everything after the closing fence (skip one blank line if present).
        var bodyStart = end + 1;
        if (bodyStart < lines.Length && string.IsNullOrWhiteSpace(lines[bodyStart]))
            bodyStart++;

        var body = string.Join('\n', lines[bodyStart..]).TrimEnd();
        return (meta, body);
    }

    private static Dictionary<string, object> ParseKeyValues(ReadOnlySpan<string> lines)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        string? currentListKey = null;
        List<string>? currentList = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // List item: starts with whitespace then "- "
            if (currentListKey is not null && line.Length > 0 && char.IsWhiteSpace(line[0]))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("- "))
                {
                    currentList!.Add(trimmed[2..].Trim().Trim('"').Trim('\''));
                    continue;
                }
            }

            // Flush any open list.
            if (currentListKey is not null)
            {
                result[currentListKey] = currentList!;
                currentListKey = null;
                currentList = null;
            }

            var colonIdx = line.IndexOf(':');
            if (colonIdx < 0) continue;

            var key = line[..colonIdx].Trim();
            var value = line[(colonIdx + 1)..].Trim();

            if (string.IsNullOrEmpty(value))
            {
                // Bare key with no value → start collecting a list.
                currentListKey = key;
                currentList = [];
                continue;
            }

            // Strip surrounding quotes.
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            result[key] = value;
        }

        // Flush trailing list.
        if (currentListKey is not null)
            result[currentListKey] = currentList!;

        return result;
    }
}