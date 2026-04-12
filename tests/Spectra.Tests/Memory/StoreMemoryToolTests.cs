using Spectra.Contracts.Memory;
using Spectra.Contracts.State;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Memory;

public class StoreMemoryToolTests
{
    private readonly InMemoryMemoryStore _store = new();

    private readonly WorkflowState _state = new()
    {
        RunId = "test-run",
        WorkflowId = "test-wf"
    };

    private StoreMemoryTool CreateTool(string defaultNs = "global") =>
        new(_store, defaultNs);

    [Fact]
    public async Task Store_CreatesNewEntry()
    {
        var tool = CreateTool();
        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["key"] = "user-lang",
                ["content"] = "User prefers French"
            },
            _state);

        Assert.True(result.Success);
        Assert.Contains("created", result.Content);

        var entry = await _store.GetAsync("global", "user-lang");
        Assert.NotNull(entry);
        Assert.Equal("User prefers French", entry!.Content);
    }

    [Fact]
    public async Task Store_UpdatesExistingEntry()
    {
        await _store.SetAsync("global", "user-lang", new MemoryEntry
        {
            Key = "user-lang",
            Namespace = "global",
            Content = "English"
        });

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["key"] = "user-lang",
                ["content"] = "French"
            },
            _state);

        Assert.True(result.Success);
        Assert.Contains("updated", result.Content);

        var entry = await _store.GetAsync("global", "user-lang");
        Assert.Equal("French", entry!.Content);
    }

    [Fact]
    public async Task Store_WithNamespace_UsesCorrectScope()
    {
        var tool = CreateTool();
        await tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["key"] = "pref",
                ["content"] = "Dark mode",
                ["namespace"] = "user.alice"
            },
            _state);

        var entry = await _store.GetAsync("user.alice", "pref");
        Assert.NotNull(entry);
        Assert.Equal("Dark mode", entry!.Content);

        var global = await _store.GetAsync("global", "pref");
        Assert.Null(global);
    }

    [Fact]
    public async Task Store_WithTags_PersistsTags()
    {
        var tool = CreateTool();
        await tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["key"] = "fact",
                ["content"] = "Important data",
                ["tags"] = "finance, quarterly"
            },
            _state);

        var entry = await _store.GetAsync("global", "fact");
        Assert.NotNull(entry);
        Assert.Contains("finance", entry!.Tags);
        Assert.Contains("quarterly", entry.Tags);
    }

    [Fact]
    public async Task Store_SetsMetadataFromState()
    {
        var tool = CreateTool();
        await tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["key"] = "meta-test",
                ["content"] = "data"
            },
            _state);

        var entry = await _store.GetAsync("global", "meta-test");
        Assert.NotNull(entry);
        Assert.Equal("agent", entry!.Metadata["source"]);
        Assert.Equal("test-run", entry.Metadata["runId"]);
        Assert.Equal("test-wf", entry.Metadata["workflowId"]);
    }

    [Fact]
    public async Task Store_MissingKey_ReturnsError()
    {
        var tool = CreateTool();
        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["content"] = "data" },
            _state);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Store_MissingContent_ReturnsError()
    {
        var tool = CreateTool();
        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["key"] = "k" },
            _state);

        Assert.False(result.Success);
    }

    [Fact]
    public void Definition_HasCorrectStructure()
    {
        var tool = CreateTool();
        Assert.Equal("store_memory", tool.Definition.Name);
        Assert.Contains(tool.Definition.Parameters, p => p.Name == "key" && p.Required);
        Assert.Contains(tool.Definition.Parameters, p => p.Name == "content" && p.Required);
    }
}