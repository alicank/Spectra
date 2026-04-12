using Spectra.Contracts.State;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class TransferToAgentToolTests
{
    [Fact]
    public void Name_is_transfer_to_agent()
    {
        var tool = new TransferToAgentTool(["agent-b", "agent-c"]);

        Assert.Equal("transfer_to_agent", tool.Name);
    }

    [Fact]
    public void Definition_contains_correct_parameters()
    {
        var tool = new TransferToAgentTool(["agent-b", "agent-c"]);
        var def = tool.Definition;

        Assert.Equal("transfer_to_agent", def.Name);
        Assert.Contains(def.Parameters, p => p.Name == "target_agent" && p.Required);
        Assert.Contains(def.Parameters, p => p.Name == "intent" && p.Required);
        Assert.Contains(def.Parameters, p => p.Name == "context" && !p.Required);
        Assert.Contains(def.Parameters, p => p.Name == "constraints" && !p.Required);
    }

    [Fact]
    public void Definition_description_lists_allowed_targets()
    {
        var tool = new TransferToAgentTool(["researcher", "reviewer"]);
        var def = tool.Definition;

        Assert.Contains("researcher", def.Description);
        Assert.Contains("reviewer", def.Description);
    }

    [Fact]
    public void Target_agent_parameter_description_lists_targets()
    {
        var tool = new TransferToAgentTool(["agent-x", "agent-y"]);
        var targetParam = tool.Definition.Parameters.First(p => p.Name == "target_agent");

        Assert.Contains("agent-x", targetParam.Description);
        Assert.Contains("agent-y", targetParam.Description);
    }

    [Fact]
    public async Task ExecuteAsync_returns_fail_because_tool_is_intercepted()
    {
        var tool = new TransferToAgentTool(["agent-b"]);
        var state = new WorkflowState { WorkflowId = "wf-1" };

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>(), state);

        Assert.False(result.Success);
        Assert.Contains("intercepted", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}