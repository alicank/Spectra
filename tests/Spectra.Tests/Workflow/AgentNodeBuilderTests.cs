using Spectra.Workflow;
using Xunit;

namespace Spectra.Tests.Workflow;

public class AgentNodeBuilderTests
{
    [Fact]
    public void Build_sets_step_type_to_agent()
    {
        var definition = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("agent-node", "agent-1")
            .AddNode("end", "echo")
            .Build();

        var node = definition.Nodes[0];
        Assert.Equal("agent", node.StepType);
    }

    [Fact]
    public void Build_sets_agent_id()
    {
        var definition = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("agent-node", "my-agent")
            .Build();

        Assert.Equal("my-agent", definition.Nodes[0].AgentId);
    }

    [Fact]
    public void WithTools_sets_tools_parameter()
    {
        var definition = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("agent-node", "agent-1", n => n
                .WithTools("search", "edit", "read"))
            .Build();

        var tools = definition.Nodes[0].Parameters["tools"] as string[];
        Assert.NotNull(tools);
        Assert.Equal(["search", "edit", "read"], tools);
    }

    [Fact]
    public void WithUserPrompt_sets_parameter()
    {
        var definition = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("agent-node", "agent-1", n => n
                .WithUserPrompt("Do the thing"))
            .Build();

        Assert.Equal("Do the thing", definition.Nodes[0].Parameters["userPrompt"]);
    }

    [Fact]
    public void WithUserPromptRef_sets_parameter()
    {
        var definition = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("agent-node", "agent-1", n => n
                .WithUserPromptRef("prompts/task"))
            .Build();

        Assert.Equal("prompts/task", definition.Nodes[0].Parameters["userPromptRef"]);
    }

    [Fact]
    public void WithMaxIterations_sets_parameter()
    {
        var definition = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("agent-node", "agent-1", n => n
                .WithMaxIterations(25))
            .Build();

        Assert.Equal(25, definition.Nodes[0].Parameters["maxIterations"]);
    }

    [Fact]
    public void Default_maxIterations_is_10()
    {
        var definition = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("agent-node", "agent-1")
            .Build();

        Assert.Equal(10, definition.Nodes[0].Parameters["maxIterations"]);
    }

    [Fact]
    public void WithTokenBudget_sets_parameter()
    {
        var definition = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("agent-node", "agent-1", n => n
                .WithTokenBudget(50_000))
            .Build();

        Assert.Equal(50_000, definition.Nodes[0].Parameters["tokenBudget"]);
    }

    [Fact]
    public void WithOutputSchema_sets_parameter()
    {
        var schema = """{"type":"object","properties":{"result":{"type":"string"}}}""";

        var definition = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("agent-node", "agent-1", n => n
                .WithOutputSchema(schema))
            .Build();

        Assert.Equal(schema, definition.Nodes[0].Parameters["outputSchema"]);
    }

    [Fact]
    public void Inherits_node_builder_capabilities()
    {
        var definition = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("agent-node", "agent-1", n => n
                .WithTools("search")
                .WithUserPrompt("Go")
                .WaitForAll()
                .MapInput("task", "Context.Task")
                .MapOutput("result", "Context.Result")
                .InterruptBefore("Review before agent runs")
                .InterruptAfter("Review agent output"))
            .Build();

        var node = definition.Nodes[0];
        Assert.True(node.WaitForAll);
        Assert.Equal("Context.Task", node.InputMappings["task"]);
        Assert.Equal("Context.Result", node.OutputMappings["result"]);
        Assert.Equal("Review before agent runs", node.InterruptBefore);
        Assert.Equal("Review agent output", node.InterruptAfter);
    }

    [Fact]
    public void Full_builder_ergonomics_example()
    {
        var definition = WorkflowBuilder.Create("code-review")
            .WithName("Code Review Pipeline")
            .AddAgent("reviewer", "anthropic", "claude-sonnet-4-20250514", a => a
                .WithSystemPrompt("You are a senior code reviewer.")
                .WithMaxTokens(4096))
            .AddAgentNode("review", "reviewer", n => n
                .WithTools("read_file", "search_code", "write_review")
                .WithUserPrompt("Review the PR at {{prUrl}}")
                .WithMaxIterations(15)
                .WithTokenBudget(100_000)
                .MapOutput("response", "Context.Review"))
            .AddNode("summarize", "prompt", n => n
                .WithAgent("reviewer")
                .WithParameter("userPrompt", "Summarize: "))
            .AddEdge("review", "summarize")
            .Build();

        Assert.Equal("code-review", definition.Id);
        Assert.Equal("Code Review Pipeline", definition.Name);
        Assert.Equal(2, definition.Nodes.Count);
        Assert.Single(definition.Edges);
        Assert.Single(definition.Agents);

        var agentNode = definition.Nodes[0];
        Assert.Equal("agent", agentNode.StepType);
        Assert.Equal("reviewer", agentNode.AgentId);

        var tools = agentNode.Parameters["tools"] as string[];
        Assert.Equal(3, tools!.Length);
        Assert.Equal(15, agentNode.Parameters["maxIterations"]);
        Assert.Equal(100_000, agentNode.Parameters["tokenBudget"]);
    }

    [Fact]
    public void AddAgentNode_returns_same_builder_for_fluent_chaining()
    {
        var builder = WorkflowBuilder.Create("wf-chain");

        var returned = builder.AddAgentNode("a", "agent-1", n => n.WithTools("t"));

        Assert.Same(builder, returned);
    }
}