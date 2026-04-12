using Spectra.Contracts.Workflow;
using Spectra.Workflow;
using Xunit;

namespace Spectra.Tests.Workflow;

public class WorkflowBuilderTests
{
    [Fact]
    public void Build_WithSingleNode_ReturnsValidDefinition()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("wf-1")
            .AddNode("step1", "echo")
            .Build();

        // Assert
        Assert.Equal("wf-1", definition.Id);
        Assert.Single(definition.Nodes);
        Assert.Equal("step1", definition.Nodes[0].Id);
        Assert.Equal("echo", definition.Nodes[0].StepType);
    }

    [Fact]
    public void Build_WithNoNodes_ThrowsInvalidOperation()
    {
        // Arrange
        var builder = WorkflowBuilder.Create("wf-empty");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Equal("Workflow must contain at least one node.", ex.Message);
    }

    [Fact]
    public void Build_WithInvalidEntryNode_ThrowsInvalidOperation()
    {
        // Arrange
        var builder = WorkflowBuilder.Create("wf-bad-entry")
            .AddNode("step1", "echo")
            .SetEntryNode("nonexistent");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("nonexistent", ex.Message);
    }

    [Fact]
    public void Build_DefaultsEntryNodeToFirstNode()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("wf-default-entry")
            .AddNode("first", "echo")
            .AddNode("second", "echo")
            .Build();

        // Assert
        Assert.Equal("first", definition.EntryNodeId);
    }

    [Fact]
    public void SetEntryNode_OverridesDefault()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("wf-entry")
            .AddNode("first", "echo")
            .AddNode("second", "echo")
            .SetEntryNode("second")
            .Build();

        // Assert
        Assert.Equal("second", definition.EntryNodeId);
    }

    [Fact]
    public void Build_AppliesDefaultSettings()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("wf-defaults")
            .AddNode("step1", "echo")
            .Build();

        // Assert
        Assert.Equal(1, definition.Version);
        Assert.Equal(4, definition.MaxConcurrency);
        Assert.Equal(TimeSpan.FromMinutes(5), definition.DefaultTimeout);
        Assert.Equal(100, definition.MaxNodeIterations);
        Assert.Null(definition.Name);
        Assert.Null(definition.Description);
    }

    [Fact]
    public void Build_WithAllMetadata_SetsProperties()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("wf-meta")
            .WithName("My Workflow")
            .WithDescription("A test workflow")
            .WithVersion(3)
            .WithMaxConcurrency(8)
            .WithDefaultTimeout(TimeSpan.FromSeconds(30))
            .WithMaxNodeIterations(50)
            .AddNode("step1", "echo")
            .Build();

        // Assert
        Assert.Equal("My Workflow", definition.Name);
        Assert.Equal("A test workflow", definition.Description);
        Assert.Equal(3, definition.Version);
        Assert.Equal(8, definition.MaxConcurrency);
        Assert.Equal(TimeSpan.FromSeconds(30), definition.DefaultTimeout);
        Assert.Equal(50, definition.MaxNodeIterations);
    }

    [Fact]
    public void AddNode_WithConfiguration_SetsNodeProperties()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("wf-node-cfg")
            .AddNode("step1", "llm-call", node => node
                .WithAgent("agent-1")
                .WaitForAll()
                .WithParameter("temperature", 0.5)
                .WithParameter("format", "json")
                .MapInput("prompt", "Context.UserInput")
                .MapOutput("result", "Context.LlmOutput"))
            .Build();

        // Assert
        var node = definition.Nodes[0];
        Assert.Equal("agent-1", node.AgentId);
        Assert.True(node.WaitForAll);
        Assert.Equal(0.5, node.Parameters["temperature"]);
        Assert.Equal("json", node.Parameters["format"]);
        Assert.Equal("Context.UserInput", node.InputMappings["prompt"]);
        Assert.Equal("Context.LlmOutput", node.OutputMappings["result"]);
    }

    [Fact]
    public void AddNode_WithoutConfiguration_UsesDefaults()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("wf-node-defaults")
            .AddNode("step1", "echo")
            .Build();

        // Assert
        var node = definition.Nodes[0];
        Assert.Null(node.AgentId);
        Assert.False(node.WaitForAll);
        Assert.Empty(node.Parameters);
        Assert.Empty(node.InputMappings);
        Assert.Empty(node.OutputMappings);
    }

    [Fact]
    public void AddEdge_CreatesDirectedEdge()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("wf-edges")
            .AddNode("a", "echo")
            .AddNode("b", "echo")
            .AddEdge("a", "b")
            .Build();

        // Assert
        Assert.Single(definition.Edges);
        Assert.Equal("a", definition.Edges[0].From);
        Assert.Equal("b", definition.Edges[0].To);
        Assert.Null(definition.Edges[0].Condition);
        Assert.False(definition.Edges[0].IsLoopback);
    }

    [Fact]
    public void AddEdge_WithConditionAndLoopback_SetsProperties()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("wf-cond-edge")
            .AddNode("a", "echo")
            .AddNode("b", "echo")
            .AddEdge("a", "b", condition: "Context.HasMore == true", isLoopback: true)
            .Build();

        // Assert
        var edge = definition.Edges[0];
        Assert.Equal("Context.HasMore == true", edge.Condition);
        Assert.True(edge.IsLoopback);
    }

    [Fact]
    public void AddAgent_WithConfiguration_SetsAllFields()
    {
        // Arrange & Act
        var builder = WorkflowBuilder.Create("wf-agent")
            .AddNode("step1", "echo")
            .AddAgent("coder", "anthropic", "claude-sonnet-4-20250514", agent => agent
                .WithTemperature(0.3)
                .WithMaxTokens(4096)
                .WithSystemPrompt("You are a coder.")
                .WithSystemPromptRef("prompts/coder")
                .WithApiKeyEnvVar("ANTHROPIC_API_KEY")
                .WithApiKeyRef("vault://keys/anthropic")
                .WithApiVersionOverride("2025-01-01")
                .WithBaseUrlOverride("https://custom.api.com")
                .WithAlternativeModel("claude-haiku-4-5-20251001")
                .WithAlternativeModel("gpt-4o"));

        // Build succeeds (agents collected but not yet emitted to definition)
        var definition = builder.Build();
        Assert.NotNull(definition);
    }

    [Fact]
    public void AddStateField_WithType_AddsFieldDefinition()
    {
        // Arrange & Act
        var builder = WorkflowBuilder.Create("wf-state")
            .AddNode("step1", "echo")
            .AddStateField("Context.Messages", typeof(List<string>), "append");

        // Build succeeds (state fields collected but not yet emitted to definition)
        var definition = builder.Build();
        Assert.NotNull(definition);
    }

    [Fact]
    public void AddStateField_Generic_AddsFieldDefinition()
    {
        // Arrange & Act
        var builder = WorkflowBuilder.Create("wf-state-generic")
            .AddNode("step1", "echo")
            .AddStateField<int>("Context.Counter");

        // Build succeeds
        var definition = builder.Build();
        Assert.NotNull(definition);
    }

    [Fact]
    public void Build_MultiNodeGraph_ProducesCorrectStructure()
    {
        // Arrange & Act
        var definition = WorkflowBuilder.Create("pipeline")
            .WithName("Review Pipeline")
            .AddNode("parse", "file-reader")
            .AddNode("analyze", "llm-call", n => n.WithAgent("reviewer"))
            .AddNode("report", "formatter", n => n
                .MapInput("data", "Context.Analysis")
                .MapOutput("output", "Context.Report"))
            .AddEdge("parse", "analyze")
            .AddEdge("analyze", "report")
            .SetEntryNode("parse")
            .Build();

        // Assert
        Assert.Equal("pipeline", definition.Id);
        Assert.Equal("Review Pipeline", definition.Name);
        Assert.Equal(3, definition.Nodes.Count);
        Assert.Equal(2, definition.Edges.Count);
        Assert.Equal("parse", definition.EntryNodeId);
    }

    [Fact]
    public void FluentChaining_ReturnsSameBuilderInstance()
    {
        // Arrange
        var builder = WorkflowBuilder.Create("wf-chain");

        // Act
        var returned = builder
            .WithName("test")
            .WithDescription("desc")
            .WithVersion(2)
            .WithMaxConcurrency(2)
            .WithDefaultTimeout(TimeSpan.FromSeconds(10))
            .WithMaxNodeIterations(10)
            .SetEntryNode("step1")
            .AddNode("step1", "echo")
            .AddEdge("step1", "step1")
            .AddAgent("a1", "openai", "gpt-4o")
            .AddStateField<string>("Context.Name");

        // Assert — all methods return the same builder
        Assert.Same(builder, returned);
    }
}