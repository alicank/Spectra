using Spectra.Contracts.Memory;
using Spectra.Contracts.State;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Memory;

public class RecallMemoryToolTests
{
    private readonly InMemoryMemoryStore _store = new();
    private readonly WorkflowState _state = new();

    private RecallMemoryTool CreateTool(string defaultNs = "global") =>
        new(_store, defaultNs);

    [Fact]
    public async Task Recall_WithQuery_FindsMatchingEntries()
    {
        await _store.SetAsync("global", "fact-1", new MemoryEntry
        {
            Key = "fact-1",
            Namespace = "global",
            Content = "The project deadline is March 15th"
        });
        await _store.SetAsync("global", "fact-2", new MemoryEntry
        {
            Key = "fact-2",
            Namespace = "global",
            Content = "The team prefers TypeScript"
        });

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "deadline" },
            _state);

        Assert.True(result.Success);
        Assert.Contains("deadline", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recall_NoMatches_ReturnsEmptyMessage()
    {
        var tool = CreateTool();
        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "nonexistent" },
            _state);

        Assert.True(result.Success);
        Assert.Contains("No memories found", result.Content);
    }

    [Fact]
    public async Task Recall_MissingQuery_ReturnsError()
    {
        var tool = CreateTool();
        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?>(),
            _state);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Recall_WithNamespace_SearchesCorrectScope()
    {
        await _store.SetAsync("user.alice", "pref", new MemoryEntry
        {
            Key = "pref",
            Namespace = "user.alice",
            Content = "Prefers dark mode"
        });
        await _store.SetAsync("user.bob", "pref", new MemoryEntry
        {
            Key = "pref",
            Namespace = "user.bob",
            Content = "Prefers light mode"
        });

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["query"] = "mode",
                ["namespace"] = "user.alice"
            },
            _state);

        Assert.True(result.Success);
        Assert.Contains("dark", result.Content);
        Assert.DoesNotContain("light", result.Content);
    }

    [Fact]
    public async Task Recall_WithTags_FiltersCorrectly()
    {
        await _store.SetAsync("global", "e1", new MemoryEntry
        {
            Key = "e1",
            Namespace = "global",
            Content = "Important fact",
            Tags = ["finance"]
        });
        await _store.SetAsync("global", "e2", new MemoryEntry
        {
            Key = "e2",
            Namespace = "global",
            Content = "Another fact",
            Tags = ["tech"]
        });

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["query"] = "fact",
                ["tags"] = "finance"
            },
            _state);

        Assert.True(result.Success);
        Assert.Contains("e1", result.Content);
        Assert.DoesNotContain("e2", result.Content);
    }

    [Fact]
    public void Definition_HasCorrectStructure()
    {
        var tool = CreateTool();
        Assert.Equal("recall_memory", tool.Definition.Name);
        Assert.NotEmpty(tool.Definition.Description);
        Assert.Contains(tool.Definition.Parameters, p => p.Name == "query" && p.Required);
    }
}