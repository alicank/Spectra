using Spectra.Contracts.Memory;
using Spectra.Contracts.Steps;

namespace Spectra.Kernel.Execution;

/// <summary>
/// DAG step that persists data to long-term memory.
/// Use this as an explicit node in non-agentic workflows.
/// <para>
/// Inputs:
///   <c>namespace</c> — memory scope (defaults to "global").
///   <c>key</c> — unique key for the entry (required).
///   <c>content</c> — the data to store (required; will be serialized if not a string).
///   <c>tags</c> — comma-separated tags (optional).
/// </para>
/// <para>
/// Outputs:
///   <c>stored</c> — boolean indicating success.
///   <c>key</c> — the key that was stored.
///   <c>action</c> — "created" or "updated".
/// </para>
/// </summary>
public class MemoryStoreStep : IStep
{
    public string StepType => "memory.store";

    public async Task<StepResult> ExecuteAsync(StepContext context)
    {
        if (context.Memory is null)
            return StepResult.Fail("No memory store configured. Register an IMemoryStore to use memory.store.");

        var ns = GetStringInput(context, "namespace") ?? "global";
        var key = GetStringInput(context, "key");
        var content = GetStringInput(context, "content");

        if (string.IsNullOrWhiteSpace(key))
            return StepResult.Fail("The 'key' input is required for memory.store.");
        if (string.IsNullOrWhiteSpace(content))
            return StepResult.Fail("The 'content' input is required for memory.store.");

        var tags = new List<string>();
        var tagsRaw = GetStringInput(context, "tags");
        if (!string.IsNullOrEmpty(tagsRaw))
            tags.AddRange(tagsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        try
        {
            var existing = await context.Memory.GetAsync(ns, key, context.CancellationToken);

            var entry = new MemoryEntry
            {
                Key = key,
                Namespace = ns,
                Content = content,
                Tags = tags,
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "step",
                    ["nodeId"] = context.NodeId,
                    ["runId"] = context.RunId,
                    ["workflowId"] = context.WorkflowId
                },
                CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await context.Memory.SetAsync(ns, key, entry, context.CancellationToken);

            var action = existing is not null ? "updated" : "created";
            return StepResult.Success(new Dictionary<string, object?>
            {
                ["stored"] = true,
                ["key"] = key,
                ["action"] = action
            });
        }
        catch (Exception ex)
        {
            return StepResult.Fail($"Memory store failed: {ex.Message}", ex);
        }
    }

    private static string? GetStringInput(StepContext context, string key) =>
        context.Inputs.TryGetValue(key, out var v) ? v?.ToString() : null;
}