using Spectra.Contracts.Workflow;
using Spectra.Kernel.Validation;
using Xunit;

namespace Spectra.Tests.Validation;

public class WorkflowValidatorTests
{
    // ────────────────── Helpers ──────────────────

    private static NodeDefinition Node(string id, string stepType = "noop")
        => new() { Id = id, StepType = stepType };

    private static EdgeDefinition Edge(string from, string to, bool isLoopback = false, string? condition = null)
        => new() { From = from, To = to, IsLoopback = isLoopback, Condition = condition };

    private static WorkflowDefinition Workflow(
        string id,
        List<NodeDefinition> nodes,
        List<EdgeDefinition> edges,
        string? entryNodeId = null,
        List<AgentDefinition>? agents = null,
        List<SubgraphDefinition>? subgraphs = null)
        => new()
        {
            Id = id,
            Nodes = nodes,
            Edges = edges,
            EntryNodeId = entryNodeId,
            Agents = agents ?? [],
            Subgraphs = subgraphs ?? []
        };

    // ────────────────── Valid workflows ──────────────────

    [Fact]
    public void Validate_LinearChain_IsValid()
    {
        var wf = Workflow("linear", [Node("a"), Node("b"), Node("c")],
            [Edge("a", "b"), Edge("b", "c")], entryNodeId: "a");

        var result = WorkflowValidator.Validate(wf);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_SingleNode_IsValid()
    {
        var wf = Workflow("single", [Node("only")], [], entryNodeId: "only");

        var result = WorkflowValidator.Validate(wf);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_LoopbackEdge_IsValid()
    {
        var wf = Workflow("loop", [Node("a"), Node("b")],
            [Edge("a", "b"), Edge("b", "a", isLoopback: true)], entryNodeId: "a");

        var result = WorkflowValidator.Validate(wf);

        Assert.True(result.IsValid);
    }

    // ────────────────── Errors ──────────────────

    [Fact]
    public void Validate_EmptyWorkflow_ReturnsError()
    {
        var wf = Workflow("empty", [], []);

        var result = WorkflowValidator.Validate(wf);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("no nodes"));
    }

    [Fact]
    public void Validate_DuplicateNodeIds_ReturnsError()
    {
        var wf = Workflow("dup", [Node("a"), Node("a")], []);

        var result = WorkflowValidator.Validate(wf);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate"));
    }

    [Fact]
    public void Validate_InvalidEntryNode_ReturnsError()
    {
        var wf = Workflow("bad-entry", [Node("a")], [], entryNodeId: "ghost");

        var result = WorkflowValidator.Validate(wf);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ghost"));
    }

    [Fact]
    public void Validate_UnknownEdgeSource_ReturnsError()
    {
        var wf = Workflow("bad-edge", [Node("a")], [Edge("ghost", "a")]);

        var result = WorkflowValidator.Validate(wf);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ghost"));
    }

    [Fact]
    public void Validate_UnknownEdgeTarget_ReturnsError()
    {
        var wf = Workflow("bad-edge-target", [Node("a")], [Edge("a", "ghost")]);

        var result = WorkflowValidator.Validate(wf);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ghost"));
    }

    [Fact]
    public void Validate_CycleWithoutLoopback_ReturnsError()
    {
        var wf = Workflow("cycle", [Node("a"), Node("b"), Node("c")],
            [Edge("a", "b"), Edge("b", "c"), Edge("c", "a")], entryNodeId: "a");

        var result = WorkflowValidator.Validate(wf);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("cycle"));
    }

    [Fact]
    public void Validate_SelfEdgeWithoutLoopback_ReturnsError()
    {
        var wf = Workflow("self-edge", [Node("a")],
            [Edge("a", "a")], entryNodeId: "a");

        var result = WorkflowValidator.Validate(wf);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("loopback"));
    }

    [Fact]
    public void Validate_UndefinedAgent_ReturnsError()
    {
        var node = Node("a", "prompt");
        node.AgentId = "missing-agent";
        var wf = Workflow("bad-agent", [node], [], entryNodeId: "a");

        var result = WorkflowValidator.Validate(wf);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("missing-agent"));
    }

    [Fact]
    public void Validate_UndefinedSubgraph_ReturnsError()
    {
        var node = Node("a", "subgraph");
        node.SubgraphId = "missing-subgraph";
        var wf = Workflow("bad-sub", [node], [], entryNodeId: "a");

        var result = WorkflowValidator.Validate(wf);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("missing-subgraph"));
    }

    // ────────────────── Subgraph circular dependency ──────────────────

    [Fact]
    public void Validate_SubgraphReferencesParent_ReturnsError()
    {
        // Parent workflow "parent-wf" has a subgraph whose child workflow ID is "parent-wf"
        var childWorkflow = new WorkflowDefinition
        {
            Id = "parent-wf", // Same as parent — circular!
            Nodes = [Node("child-step")],
            Edges = []
        };

        var subgraph = new SubgraphDefinition
        {
            Id = "child-sub",
            Workflow = childWorkflow
        };

        var node = Node("run-sub", "subgraph");
        node.SubgraphId = "child-sub";

        var wf = Workflow("parent-wf",
            [node],
            [],
            entryNodeId: "run-sub",
            subgraphs: [subgraph]);

        var result = WorkflowValidator.Validate(wf);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("circular dependency"));
    }

    [Fact]
    public void Validate_TransitiveSubgraphCircle_ReturnsError()
    {
        // parent-wf -> sub-a -> sub-b -> parent-wf (transitive)
        var grandchildWorkflow = new WorkflowDefinition
        {
            Id = "parent-wf", // Back to parent — circular!
            Nodes = [Node("gc-step")],
            Edges = []
        };

        var childWorkflow = new WorkflowDefinition
        {
            Id = "child-wf",
            Nodes = [new NodeDefinition { Id = "run-gc", StepType = "subgraph", SubgraphId = "sub-b" }],
            Edges = [],
            Subgraphs = [new SubgraphDefinition { Id = "sub-b", Workflow = grandchildWorkflow }]
        };

        var subgraphA = new SubgraphDefinition
        {
            Id = "sub-a",
            Workflow = childWorkflow
        };

        var node = Node("run-sub", "subgraph");
        node.SubgraphId = "sub-a";

        var wf = Workflow("parent-wf",
            [node],
            [],
            entryNodeId: "run-sub",
            subgraphs: [subgraphA]);

        var result = WorkflowValidator.Validate(wf);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("circular dependency"));
    }

    // ────────────────── Warnings ──────────────────

    [Fact]
    public void Validate_FanOut_ReturnsWarning()
    {
        // store-findings fans out to risk-assessment AND regulatory-impact
        var wf = Workflow("fan-out",
            [Node("start"), Node("branch-a"), Node("branch-b")],
            [Edge("start", "branch-a"), Edge("start", "branch-b")],
            entryNodeId: "start");

        var result = WorkflowValidator.Validate(wf);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("fans out"));
        Assert.Contains(result.Warnings, w => w.Contains("ParallelScheduler"));
    }

    [Fact]
    public void Validate_ConditionalFanOut_NoWarning()
    {
        // Conditional edges are fine — only one path is taken
        var wf = Workflow("conditional-fan",
            [Node("start"), Node("branch-a"), Node("branch-b")],
            [
                Edge("start", "branch-a", condition: "Context.Type == 'A'"),
                Edge("start", "branch-b", condition: "Context.Type == 'B'")
            ],
            entryNodeId: "start");

        var result = WorkflowValidator.Validate(wf);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("fans out"));
    }

    [Fact]
    public void Validate_UnreachableNode_ReturnsWarning()
    {
        var wf = Workflow("unreachable",
            [Node("a"), Node("b"), Node("orphan")],
            [Edge("a", "b")],
            entryNodeId: "a");

        var result = WorkflowValidator.Validate(wf);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("orphan") && w.Contains("unreachable"));
    }

    [Fact]
    public void Validate_WaitForAllWithSingleEdge_ReturnsWarning()
    {
        var mergeNode = Node("merge", "merge_results");
        mergeNode.WaitForAll = true;

        var wf = Workflow("single-merge",
            [Node("a"), mergeNode],
            [Edge("a", "merge")],
            entryNodeId: "a");

        var result = WorkflowValidator.Validate(wf);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("merge") && w.Contains("waitForAll"));
    }

    // ────────────────── Diamond (valid fan-out/fan-in) ──────────────────

    [Fact]
    public void Validate_DiamondWithFanOut_WarnsAboutSequentialRunner()
    {
        var merge = Node("merge", "merge_results");
        merge.WaitForAll = true;

        var wf = Workflow("diamond",
            [Node("start"), Node("left"), Node("right"), merge],
            [
                Edge("start", "left"),
                Edge("start", "right"),
                Edge("left", "merge"),
                Edge("right", "merge")
            ],
            entryNodeId: "start");

        var result = WorkflowValidator.Validate(wf);

        Assert.True(result.IsValid);
        // Should warn about fan-out from 'start'
        Assert.Contains(result.Warnings, w => w.Contains("start") && w.Contains("fans out"));
    }
}