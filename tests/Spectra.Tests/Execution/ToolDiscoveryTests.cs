using Spectra.Contracts.State;
using Spectra.Contracts.Tools;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class ToolDiscoveryTests
{
    [Fact]
    public void DiscoverTools_finds_attributed_tools_in_assembly()
    {
        var tools = ToolDiscovery.DiscoverTools(typeof(ToolDiscoveryTests).Assembly);

        Assert.Contains(tools, t => t.Name == "discoverable_test_tool");
    }

    [Fact]
    public void DiscoverTools_ignores_tools_without_attribute()
    {
        var tools = ToolDiscovery.DiscoverTools(typeof(ToolDiscoveryTests).Assembly);

        Assert.DoesNotContain(tools, t => t.Name == "non_attributed_tool");
    }

    [Fact]
    public void DiscoverTools_ignores_abstract_classes()
    {
        var tools = ToolDiscovery.DiscoverTools(typeof(ToolDiscoveryTests).Assembly);

        Assert.DoesNotContain(tools, t => t.Name == "abstract_tool");
    }

    [Fact]
    public void DiscoverTools_generic_finds_tools_from_marker_type()
    {
        var tools = ToolDiscovery.DiscoverTools<DiscoverableTestTool>();

        Assert.Contains(tools, t => t.Name == "discoverable_test_tool");
    }
}

// ── Test fixtures ──

[SpectraTool]
public class DiscoverableTestTool : ITool
{
    public string Name => "discoverable_test_tool";
    public ToolDefinition Definition => new()
    {
        Name = "discoverable_test_tool",
        Description = "A discoverable test tool"
    };

    public Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> arguments,
        WorkflowState state,
        CancellationToken ct = default)
        => Task.FromResult(ToolResult.Ok("discovered"));
}

// No attribute — should NOT be discovered
public class NonAttributedTool : ITool
{
    public string Name => "non_attributed_tool";
    public ToolDefinition Definition => new()
    {
        Name = "non_attributed_tool",
        Description = "Should not be discovered"
    };

    public Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> arguments,
        WorkflowState state,
        CancellationToken ct = default)
        => Task.FromResult(ToolResult.Ok("no"));
}

// Abstract — should NOT be discovered
[SpectraTool]
public abstract class AbstractTestTool : ITool
{
    public string Name => "abstract_tool";
    public ToolDefinition Definition => new()
    {
        Name = "abstract_tool",
        Description = "Abstract"
    };

    public abstract Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> arguments,
        WorkflowState state,
        CancellationToken ct = default);
}