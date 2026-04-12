using System.Text.Json;
using Spectra.Contracts.Memory;
using Spectra.Contracts.State;
using Spectra.Contracts.Tools;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Internal tool that allows an LLM agent to recall memories from the long-term
/// memory store. Auto-injected when <see cref="MemoryOptions.AutoInjectAgentTools"/> is enabled.
/// </summary>
public sealed class RecallMemoryTool : ITool
{
    private readonly IMemoryStore _memoryStore;
    private readonly string _defaultNamespace;

    public string Name => "recall_memory";

    public RecallMemoryTool(IMemoryStore memoryStore, string defaultNamespace = "global")
    {
        _memoryStore = memoryStore;
        _defaultNamespace = defaultNamespace;
    }

    public ToolDefinition Definition => new()
    {
        Name = Name,
        Description = "Search long-term memory for relevant information from previous interactions or stored knowledge. " +
                      "Use this when you need context from past conversations, user preferences, or previously learned facts.",
        Parameters =
        [
            new ToolParameter
            {
                Name = "query",
                Type = "string",
                Description = "What to search for in memory. Can be a keyword, phrase, or natural language question.",
                Required = true
            },
            new ToolParameter
            {
                Name = "namespace",
                Type = "string",
                Description = "Memory scope to search in (e.g., 'global', 'user.alice', 'workflow.my-flow'). " +
                              "Omit to use the default namespace.",
                Required = false
            },
            new ToolParameter
            {
                Name = "tags",
                Type = "string",
                Description = "Comma-separated tags to filter by (e.g., 'finance,quarterly').",
                Required = false
            },
            new ToolParameter
            {
                Name = "max_results",
                Type = "integer",
                Description = "Maximum number of results to return. Defaults to 5.",
                Required = false
            }
        ]
    };

    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> arguments,
        WorkflowState state,
        CancellationToken cancellationToken = default)
    {
        var queryText = arguments.TryGetValue("query", out var q) ? q?.ToString() : null;
        if (string.IsNullOrWhiteSpace(queryText))
            return ToolResult.Fail("The 'query' parameter is required.");

        var ns = arguments.TryGetValue("namespace", out var nsObj) && nsObj is not null
            ? nsObj.ToString()!
            : _defaultNamespace;

        var maxResults = 5;
        if (arguments.TryGetValue("max_results", out var mrObj) && mrObj is not null)
        {
            if (int.TryParse(mrObj.ToString(), out var parsed) && parsed > 0)
                maxResults = parsed;
        }

        List<string>? tags = null;
        if (arguments.TryGetValue("tags", out var tagsObj) && tagsObj is not null)
        {
            var raw = tagsObj.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
                tags = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        try
        {
            var query = new MemorySearchQuery
            {
                Namespace = ns,
                Text = queryText,
                Tags = tags,
                MaxResults = maxResults
            };

            var results = await _memoryStore.SearchAsync(query, cancellationToken);

            if (results.Count == 0)
            {
                // Fall back to listing if search is not supported
                if (!_memoryStore.Capabilities.CanSearch)
                {
                    var all = await _memoryStore.ListAsync(ns, cancellationToken);
                    if (all.Count == 0)
                        return ToolResult.Ok("No memories found in this namespace.");

                    var listed = all.Take(maxResults).Select(FormatEntry).ToList();
                    return ToolResult.Ok(JsonSerializer.Serialize(new
                    {
                        note = "Search not supported by this store; showing recent entries.",
                        count = listed.Count,
                        memories = listed
                    }));
                }

                return ToolResult.Ok("No memories found matching your query.");
            }

            var formatted = results.Select(r => new
            {
                key = r.Entry.Key,
                content = TruncateContent(r.Entry.Content, 500),
                tags = r.Entry.Tags,
                score = Math.Round(r.Score, 3),
                updatedAt = r.Entry.UpdatedAt.ToString("o")
            }).ToList();

            return ToolResult.Ok(JsonSerializer.Serialize(new
            {
                count = formatted.Count,
                @namespace = ns,
                memories = formatted
            }));
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Memory recall failed: {ex.Message}");
        }
    }

    private static object FormatEntry(MemoryEntry e) => new
    {
        key = e.Key,
        content = TruncateContent(e.Content, 500),
        tags = e.Tags,
        updatedAt = e.UpdatedAt.ToString("o")
    };

    private static string TruncateContent(string content, int maxLength) =>
        content.Length <= maxLength ? content : content[..maxLength] + "…";
}