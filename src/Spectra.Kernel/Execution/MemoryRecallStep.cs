using System.Text.Json;
using Spectra.Contracts.Memory;
using Spectra.Contracts.Steps;

namespace Spectra.Kernel.Execution;

/// <summary>
/// DAG step that recalls memories and injects them into workflow state.
/// Use this as an explicit node in non-agentic workflows.
/// <para>
/// Inputs:
///   <c>namespace</c> — memory scope (defaults to MemoryOptions.DefaultNamespace).
///   <c>query</c> — search text (optional; if omitted, lists recent entries).
///   <c>key</c> — exact key lookup (optional; takes precedence over query).
///   <c>tags</c> — comma-separated tag filter (optional).
///   <c>maxResults</c> — max entries to return (default 10).
/// </para>
/// <para>
/// Outputs:
///   <c>memories</c> — list of recalled <see cref="MemoryEntry"/> objects.
///   <c>count</c> — number of entries returned.
///   <c>found</c> — boolean indicating if any memories were found.
/// </para>
/// </summary>
public class MemoryRecallStep : IStep
{
    public string StepType => "memory.recall";

    public async Task<StepResult> ExecuteAsync(StepContext context)
    {
        if (context.Memory is null)
            return StepResult.Fail("No memory store configured. Register an IMemoryStore to use memory.recall.");

        var ns = GetStringInput(context, "namespace") ?? "global";
        var key = GetStringInput(context, "key");
        var queryText = GetStringInput(context, "query");
        var maxResults = GetIntInput(context, "maxResults", 10);

        try
        {
            // Exact key lookup
            if (!string.IsNullOrEmpty(key))
            {
                var entry = await context.Memory.GetAsync(ns, key, context.CancellationToken);
                var singleResult = entry is not null ? new List<MemoryEntry> { entry } : new List<MemoryEntry>();

                return StepResult.Success(new Dictionary<string, object?>
                {
                    ["memories"] = singleResult,
                    ["count"] = singleResult.Count,
                    ["found"] = entry is not null
                });
            }

            // Search or list
            if (!string.IsNullOrEmpty(queryText))
            {
                List<string>? tags = null;
                var tagsRaw = GetStringInput(context, "tags");
                if (!string.IsNullOrEmpty(tagsRaw))
                    tags = tagsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

                var query = new MemorySearchQuery
                {
                    Namespace = ns,
                    Text = queryText,
                    Tags = tags,
                    MaxResults = maxResults
                };

                var searchResults = await context.Memory.SearchAsync(query, context.CancellationToken);
                var entries = searchResults.Select(r => r.Entry).ToList();

                return StepResult.Success(new Dictionary<string, object?>
                {
                    ["memories"] = entries,
                    ["count"] = entries.Count,
                    ["found"] = entries.Count > 0
                });
            }

            // No key or query — list recent
            var all = await context.Memory.ListAsync(ns, context.CancellationToken);
            var limited = all.Take(maxResults).ToList();

            return StepResult.Success(new Dictionary<string, object?>
            {
                ["memories"] = limited,
                ["count"] = limited.Count,
                ["found"] = limited.Count > 0
            });
        }
        catch (Exception ex)
        {
            return StepResult.Fail($"Memory recall failed: {ex.Message}", ex);
        }
    }

    private static string? GetStringInput(StepContext context, string key) =>
        context.Inputs.TryGetValue(key, out var v) ? v?.ToString() : null;

    private static int GetIntInput(StepContext context, string key, int defaultValue)
    {
        if (context.Inputs.TryGetValue(key, out var v) && v is not null
            && int.TryParse(v.ToString(), out var parsed))
            return parsed;
        return defaultValue;
    }
}