using Spectra.Contracts.Memory;
using Spectra.Contracts.State;
using Spectra.Contracts.Tools;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Internal tool that allows an LLM agent to persist knowledge to long-term memory.
/// Auto-injected when <see cref="MemoryOptions.AutoInjectAgentTools"/> is enabled.
/// </summary>
public sealed class StoreMemoryTool : ITool
{
    private readonly IMemoryStore _memoryStore;
    private readonly string _defaultNamespace;

    public string Name => "store_memory";

    public StoreMemoryTool(IMemoryStore memoryStore, string defaultNamespace = "global")
    {
        _memoryStore = memoryStore;
        _defaultNamespace = defaultNamespace;
    }

    public ToolDefinition Definition => new()
    {
        Name = Name,
        Description = "Store a piece of knowledge in long-term memory for future recall. " +
                      "Use this to remember important facts, user preferences, decisions, or any information " +
                      "that should persist across conversations.",
        Parameters =
        [
            new ToolParameter
            {
                Name = "key",
                Type = "string",
                Description = "A unique identifier for this memory (e.g., 'user-preference-language', 'project-deadline'). " +
                              "Use descriptive, kebab-case names. If a key already exists, it will be overwritten.",
                Required = true
            },
            new ToolParameter
            {
                Name = "content",
                Type = "string",
                Description = "The information to remember. Be specific and self-contained — " +
                              "this should make sense when recalled later without additional context.",
                Required = true
            },
            new ToolParameter
            {
                Name = "namespace",
                Type = "string",
                Description = "Memory scope to store in (e.g., 'global', 'user.alice', 'workflow.my-flow'). " +
                              "Omit to use the default namespace.",
                Required = false
            },
            new ToolParameter
            {
                Name = "tags",
                Type = "string",
                Description = "Comma-separated tags for categorization and filtering (e.g., 'preference,language').",
                Required = false
            }
        ]
    };

    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> arguments,
        WorkflowState state,
        CancellationToken cancellationToken = default)
    {
        var key = arguments.TryGetValue("key", out var k) ? k?.ToString() : null;
        if (string.IsNullOrWhiteSpace(key))
            return ToolResult.Fail("The 'key' parameter is required.");

        var content = arguments.TryGetValue("content", out var c) ? c?.ToString() : null;
        if (string.IsNullOrWhiteSpace(content))
            return ToolResult.Fail("The 'content' parameter is required.");

        var ns = arguments.TryGetValue("namespace", out var nsObj) && nsObj is not null
            ? nsObj.ToString()!
            : _defaultNamespace;

        var tags = new List<string>();
        if (arguments.TryGetValue("tags", out var tagsObj) && tagsObj is not null)
        {
            var raw = tagsObj.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
                tags.AddRange(raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        try
        {
            // Check if entry already exists (for setting CreatedAt correctly)
            var existing = await _memoryStore.GetAsync(ns, key, cancellationToken);

            var entry = new MemoryEntry
            {
                Key = key,
                Namespace = ns,
                Content = content,
                Tags = tags,
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "agent",
                    ["runId"] = state.RunId,
                    ["workflowId"] = state.WorkflowId
                },
                CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _memoryStore.SetAsync(ns, key, entry, cancellationToken);

            var action = existing is not null ? "updated" : "created";
            return ToolResult.Ok($"Memory '{key}' {action} in namespace '{ns}'.");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to store memory: {ex.Message}");
        }
    }
}