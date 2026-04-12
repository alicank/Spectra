using Spectra.Contracts.Workflow;
using Spectra.Workflow;
using Xunit;

namespace Spectra.Tests.Workflow;

public class WorkflowBuilderSubgraphTests
{
    private static WorkflowDefinition CreateChildWorkflow() =>
        WorkflowBuilder.Create("child")
            .AddNode("c1", "noop")
            .Build();

    [Fact]
    public void AddSubgraph_and_AddSubgraphNode_builds_correctly()
    {
        var child = CreateChildWorkflow();

        var workflow = WorkflowBuilder.Create("parent")
            .AddNode("start", "noop")
            .AddSubgraph("sub1", child, sg => sg
                .MapInput("Context.docs", "items")
                .MapOutput("Artifacts.result", "Context.subResult"))
            .AddSubgraphNode("sg-node", "sub1")
            .AddEdge("start", "sg-node")
            .Build();

        Assert.Single(workflow.Subgraphs);
        Assert.Equal("sub1", workflow.Subgraphs[0].Id);
        Assert.Same(child, workflow.Subgraphs[0].Workflow);
        Assert.Equal("items", workflow.Subgraphs[0].InputMappings["Context.docs"]);
        Assert.Equal("Context.subResult", workflow.Subgraphs[0].OutputMappings["Artifacts.result"]);

        var sgNode = workflow.Nodes.First(n => n.Id == "sg-node");
        Assert.Equal("subgraph", sgNode.StepType);
        Assert.Equal("sub1", sgNode.SubgraphId);
    }

    [Fact]
    public void NodeBuilder_AsSubgraph_overrides_step_type()
    {
        var child = CreateChildWorkflow();

        var workflow = WorkflowBuilder.Create("parent")
            .AddSubgraph("sub1", child)
            .AddNode("sg-node", "anything", n => n.AsSubgraph("sub1"))
            .Build();

        var node = workflow.Nodes[0];
        Assert.Equal("subgraph", node.StepType);
        Assert.Equal("sub1", node.SubgraphId);
    }

    [Fact]
    public void SubgraphBuilder_builds_correct_mappings()
    {
        var child = CreateChildWorkflow();

        var workflow = WorkflowBuilder.Create("parent")
            .AddNode("n1", "noop")
            .AddSubgraph("sub1", child, sg => sg
                .MapInput("Inputs.query", "searchTerm")
                .MapInput("Context.config", "settings")
                .MapOutput("Context.answer", "Artifacts.answer")
                .MapOutput("Artifacts.log", "Context.subLog"))
            .AddSubgraphNode("sg-node", "sub1")
            .Build();

        var sub = workflow.Subgraphs[0];
        Assert.Equal(2, sub.InputMappings.Count);
        Assert.Equal("searchTerm", sub.InputMappings["Inputs.query"]);
        Assert.Equal("settings", sub.InputMappings["Context.config"]);
        Assert.Equal(2, sub.OutputMappings.Count);
        Assert.Equal("Artifacts.answer", sub.OutputMappings["Context.answer"]);
        Assert.Equal("Context.subLog", sub.OutputMappings["Artifacts.log"]);
    }
}