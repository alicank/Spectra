using Spectra.Contracts.State;
using Spectra.Contracts.Tools;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class InMemoryToolRegistryTests
{
    [Fact]
    public void Register_and_Get_returns_tool()
    {
        var registry = new InMemoryToolRegistry();
        var tool = new FakeTool("greet");

        registry.Register(tool);

        Assert.Same(tool, registry.Get("greet"));
    }

    [Fact]
    public void Get_returns_null_for_unknown_tool()
    {
        var registry = new InMemoryToolRegistry();
        Assert.Null(registry.Get("nope"));
    }

    [Fact]
    public void Get_is_case_insensitive()
    {
        var registry = new InMemoryToolRegistry();
        registry.Register(new FakeTool("MyTool"));

        Assert.NotNull(registry.Get("mytool"));
        Assert.NotNull(registry.Get("MYTOOL"));
    }

    [Fact]
    public void GetAll_returns_all_registered_tools()
    {
        var registry = new InMemoryToolRegistry();
        registry.Register(new FakeTool("a"));
        registry.Register(new FakeTool("b"));

        var all = registry.GetAll();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void GetDefinitions_without_filter_returns_all()
    {
        var registry = new InMemoryToolRegistry();
        registry.Register(new FakeTool("x"));
        registry.Register(new FakeTool("y"));

        var defs = registry.GetDefinitions();
        Assert.Equal(2, defs.Count);
    }

    [Fact]
    public void GetDefinitions_with_filter_returns_subset()
    {
        var registry = new InMemoryToolRegistry();
        registry.Register(new FakeTool("keep"));
        registry.Register(new FakeTool("skip"));

        var defs = registry.GetDefinitions(["keep"]);
        Assert.Single(defs);
        Assert.Equal("keep", defs[0].Name);
    }

    [Fact]
    public void Register_overwrites_existing_tool_with_same_name()
    {
        var registry = new InMemoryToolRegistry();
        var first = new FakeTool("dup");
        var second = new FakeTool("dup");

        registry.Register(first);
        registry.Register(second);

        Assert.Same(second, registry.Get("dup"));
        Assert.Single(registry.GetAll());
    }

    private class FakeTool : ITool
    {
        public FakeTool(string name)
        {
            Name = name;
            Definition = new ToolDefinition
            {
                Name = name,
                Description = $"Fake tool: {name}"
            };
        }

        public string Name { get; }
        public ToolDefinition Definition { get; }

        public Task<ToolResult> ExecuteAsync(
            Dictionary<string, object?> arguments,
            WorkflowState state,
            CancellationToken ct = default)
            => Task.FromResult(ToolResult.Ok("ok"));
    }
}