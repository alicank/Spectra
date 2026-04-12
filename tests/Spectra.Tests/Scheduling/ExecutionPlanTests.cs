using Spectra.Contracts.Workflow;
using Spectra.Kernel.Scheduling;
using Xunit;

namespace Spectra.Tests.Scheduling;

public class ExecutionPlanTests
{
    // ────────────────── Helpers ──────────────────

    private static NodeDefinition Node(string id, string stepType = "Noop")
        => new() { Id = id, StepType = stepType };

    private static EdgeDefinition Edge(string from, string to, bool isLoopback = false)
        => new() { From = from, To = to, IsLoopback = isLoopback };

    private static WorkflowDefinition Workflow(
        List<NodeDefinition> nodes,
        List<EdgeDefinition> edges,
        string? entryNodeId = null)
        => new()
        {
            Id = "test-wf",
            Nodes = nodes,
            Edges = edges,
            EntryNodeId = entryNodeId
        };

    private static IReadOnlyDictionary<string, NodeDefinition> NodeLookup(params NodeDefinition[] nodes)
        => nodes.ToDictionary(n => n.Id);

    // ────────────────── Build basics ──────────────────

    [Fact]
    public void Build_LinearChain_ProducesCorrectOrder()
    {
        // A -> B -> C
        var wf = Workflow(
            [Node("A"), Node("B"), Node("C")],
            [Edge("A", "B"), Edge("B", "C")]);

        var plan = ExecutionPlan.Build(wf);

        Assert.True(plan.IsValid);
        Assert.Equal(3, plan.TopologicalOrder.Count);

        var indexA = plan.TopologicalOrder.IndexOf("A");
        var indexB = plan.TopologicalOrder.IndexOf("B");
        var indexC = plan.TopologicalOrder.IndexOf("C");

        Assert.True(indexA < indexB);
        Assert.True(indexB < indexC);
    }

    [Fact]
    public void Build_SingleNode_IsReady()
    {
        var wf = Workflow([Node("only")], []);

        var plan = ExecutionPlan.Build(wf);

        Assert.True(plan.IsValid);
        Assert.Single(plan.GetReadyNodes());
        Assert.Equal("only", plan.GetReadyNodes().First());
        Assert.Single(plan.TopologicalOrder);
    }

    [Fact]
    public void Build_DiamondGraph_ResolvesOrder()
    {
        //     A
        //    / \
        //   B   C
        //    \ /
        //     D
        var wf = Workflow(
            [Node("A"), Node("B"), Node("C"), Node("D")],
            [Edge("A", "B"), Edge("A", "C"), Edge("B", "D"), Edge("C", "D")]);

        var plan = ExecutionPlan.Build(wf);

        Assert.True(plan.IsValid);
        Assert.Equal(4, plan.TopologicalOrder.Count);

        var idxA = plan.TopologicalOrder.IndexOf("A");
        var idxB = plan.TopologicalOrder.IndexOf("B");
        var idxC = plan.TopologicalOrder.IndexOf("C");
        var idxD = plan.TopologicalOrder.IndexOf("D");

        Assert.True(idxA < idxB);
        Assert.True(idxA < idxC);
        Assert.True(idxB < idxD);
        Assert.True(idxC < idxD);
    }

    [Fact]
    public void Build_MultipleRoots_AllMarkedReady()
    {
        // A and B are independent roots, both lead to C
        var wf = Workflow(
            [Node("A"), Node("B"), Node("C")],
            [Edge("A", "C"), Edge("B", "C")]);

        var plan = ExecutionPlan.Build(wf);

        Assert.True(plan.IsValid);
        var ready = plan.GetReadyNodes().ToList();
        Assert.Contains("A", ready);
        Assert.Contains("B", ready);
        Assert.DoesNotContain("C", ready);
    }

    [Fact]
    public void Build_WithExplicitEntryNode_OnlyEntryIsReady()
    {
        // A and B both have no deps, but entry is explicitly A
        var wf = Workflow(
            [Node("A"), Node("B"), Node("C")],
            [Edge("A", "C")],
            entryNodeId: "A");

        var plan = ExecutionPlan.Build(wf);

        Assert.True(plan.IsValid);
        var ready = plan.GetReadyNodes().ToList();
        Assert.Single(ready);
        Assert.Equal("A", ready[0]);
    }

    // ────────────────── Dependencies ──────────────────

    [Fact]
    public void Build_TracksDependenciesCorrectly()
    {
        var wf = Workflow(
            [Node("A"), Node("B"), Node("C")],
            [Edge("A", "B"), Edge("A", "C")]);

        var plan = ExecutionPlan.Build(wf);

        Assert.Empty(plan.Dependencies["A"]);
        Assert.Single(plan.Dependencies["B"]);
        Assert.Contains("A", plan.Dependencies["B"]);
        Assert.Single(plan.Dependencies["C"]);
        Assert.Contains("A", plan.Dependencies["C"]);
    }

    [Fact]
    public void Build_TracksDependentsCorrectly()
    {
        var wf = Workflow(
            [Node("A"), Node("B"), Node("C")],
            [Edge("A", "B"), Edge("A", "C")]);

        var plan = ExecutionPlan.Build(wf);

        Assert.Equal(2, plan.Dependents["A"].Count);
        Assert.Contains("B", plan.Dependents["A"]);
        Assert.Contains("C", plan.Dependents["A"]);
        Assert.Empty(plan.Dependents["B"]);
        Assert.Empty(plan.Dependents["C"]);
    }

    // ────────────────── Validation ──────────────────

    [Fact]
    public void Build_EmptyWorkflow_ReturnsValidationError()
    {
        var wf = Workflow([], []);

        var plan = ExecutionPlan.Build(wf);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.ValidationErrors, e => e.Contains("no nodes"));
    }

    [Fact]
    public void Build_DuplicateNodeIds_ReturnsValidationError()
    {
        var wf = Workflow([Node("A"), Node("A")], []);

        var plan = ExecutionPlan.Build(wf);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.ValidationErrors, e => e.Contains("Duplicate"));
    }

    [Fact]
    public void Build_EdgeReferencesUnknownSource_ReturnsValidationError()
    {
        var wf = Workflow(
            [Node("A")],
            [Edge("ghost", "A")]);

        var plan = ExecutionPlan.Build(wf);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.ValidationErrors, e => e.Contains("ghost"));
    }

    [Fact]
    public void Build_EdgeReferencesUnknownTarget_ReturnsValidationError()
    {
        var wf = Workflow(
            [Node("A")],
            [Edge("A", "ghost")]);

        var plan = ExecutionPlan.Build(wf);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.ValidationErrors, e => e.Contains("ghost"));
    }

    [Fact]
    public void Build_InvalidEntryNodeId_ReturnsValidationError()
    {
        var wf = Workflow(
            [Node("A")],
            [],
            entryNodeId: "nonexistent");

        var plan = ExecutionPlan.Build(wf);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.ValidationErrors, e => e.Contains("nonexistent"));
    }

    // ────────────────── Cycle detection ──────────────────

    [Fact]
    public void Build_CyclicGraph_DetectsCycle()
    {
        // A -> B -> C -> A (not marked as loopback)
        var wf = Workflow(
            [Node("A"), Node("B"), Node("C")],
            [Edge("A", "B"), Edge("B", "C"), Edge("C", "A")]);

        var plan = ExecutionPlan.Build(wf);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.ValidationErrors, e => e.Contains("cycle"));
    }

    [Fact]
    public void Build_LoopbackEdge_ExcludedFromCycleDetection()
    {
        // A -> B -> A (loopback), so no real cycle
        var wf = Workflow(
            [Node("A"), Node("B")],
            [Edge("A", "B"), Edge("B", "A", isLoopback: true)]);

        var plan = ExecutionPlan.Build(wf);

        Assert.True(plan.IsValid);
        Assert.Equal(2, plan.TopologicalOrder.Count);
    }

    // ────────────────── CompleteNode ──────────────────

    [Fact]
    public void CompleteNode_TransitionsDependentToReady_WaitForAll()
    {
        //   A
        //  / \
        // B   C
        //  \ /
        //   D (WaitForAll = true, default)
        var nodeD = Node("D");
        nodeD.WaitForAll = true;

        var wf = Workflow(
            [Node("A"), Node("B"), Node("C"), nodeD],
            [Edge("A", "B"), Edge("A", "C"), Edge("B", "D"), Edge("C", "D")]);

        var plan = ExecutionPlan.Build(wf);
        var lookup = NodeLookup(Node("A"), Node("B"), Node("C"), nodeD);

        // Complete A -> B and C become ready
        plan.NodeStates["A"].Status = NodeExecutionStatus.Running;
        plan.CompleteNode("A", lookup);

        Assert.Contains("B", plan.GetReadyNodes());
        Assert.Contains("C", plan.GetReadyNodes());
        Assert.Equal(NodeExecutionStatus.Pending, plan.NodeStates["D"].Status);

        // Complete B -> D still pending (waiting for C)
        plan.NodeStates["B"].Status = NodeExecutionStatus.Running;
        plan.CompleteNode("B", lookup);
        Assert.Equal(NodeExecutionStatus.Pending, plan.NodeStates["D"].Status);

        // Complete C -> D now ready
        plan.NodeStates["C"].Status = NodeExecutionStatus.Running;
        plan.CompleteNode("C", lookup);
        Assert.Equal(NodeExecutionStatus.Ready, plan.NodeStates["D"].Status);
    }

    [Fact]
    public void CompleteNode_TransitionsDependentToReady_WaitForAny()
    {
        // B and C both feed into D, but D has WaitForAll = false
        var nodeD = Node("D");
        nodeD.WaitForAll = false;

        var wf = Workflow(
            [Node("A"), Node("B"), Node("C"), nodeD],
            [Edge("A", "B"), Edge("A", "C"), Edge("B", "D"), Edge("C", "D")]);

        var plan = ExecutionPlan.Build(wf);
        var lookup = NodeLookup(Node("A"), Node("B"), Node("C"), nodeD);

        plan.NodeStates["A"].Status = NodeExecutionStatus.Running;
        plan.CompleteNode("A", lookup);

        // Complete just B -> D should be ready (WaitForAny)
        plan.NodeStates["B"].Status = NodeExecutionStatus.Running;
        plan.CompleteNode("B", lookup);

        Assert.Equal(NodeExecutionStatus.Ready, plan.NodeStates["D"].Status);
    }

    [Fact]
    public void CompleteNode_WithoutNodeDefinitions_DefaultsToWaitForAll()
    {
        var wf = Workflow(
            [Node("A"), Node("B"), Node("C")],
            [Edge("A", "C"), Edge("B", "C")]);

        var plan = ExecutionPlan.Build(wf);

        plan.NodeStates["A"].Status = NodeExecutionStatus.Running;
        plan.CompleteNode("A"); // no nodeDefinitions passed

        // C should still be pending (default WaitForAll, B not done)
        Assert.Equal(NodeExecutionStatus.Pending, plan.NodeStates["C"].Status);

        plan.NodeStates["B"].Status = NodeExecutionStatus.Running;
        plan.CompleteNode("B");

        Assert.Equal(NodeExecutionStatus.Ready, plan.NodeStates["C"].Status);
    }

    // ────────────────── FailNode ──────────────────

    [Fact]
    public void FailNode_MarksNodeAsFailed()
    {
        var wf = Workflow([Node("A"), Node("B")], [Edge("A", "B")]);
        var plan = ExecutionPlan.Build(wf);

        plan.NodeStates["A"].Status = NodeExecutionStatus.Running;
        plan.FailNode("A");

        Assert.Equal(NodeExecutionStatus.Failed, plan.NodeStates["A"].Status);
        Assert.True(plan.HasFailed());
    }

    [Fact]
    public void FailNode_WithCascadeSkip_SkipsAllDownstream()
    {
        // A -> B -> C
        var wf = Workflow(
            [Node("A"), Node("B"), Node("C")],
            [Edge("A", "B"), Edge("B", "C")]);

        var plan = ExecutionPlan.Build(wf);

        plan.NodeStates["A"].Status = NodeExecutionStatus.Running;
        plan.FailNode("A", cascadeSkip: true);

        Assert.Equal(NodeExecutionStatus.Failed, plan.NodeStates["A"].Status);
        Assert.Equal(NodeExecutionStatus.Skipped, plan.NodeStates["B"].Status);
        Assert.Equal(NodeExecutionStatus.Skipped, plan.NodeStates["C"].Status);
    }

    [Fact]
    public void FailNode_WithoutCascade_DoesNotSkipDownstream()
    {
        var wf = Workflow(
            [Node("A"), Node("B")],
            [Edge("A", "B")]);

        var plan = ExecutionPlan.Build(wf);

        plan.NodeStates["A"].Status = NodeExecutionStatus.Running;
        plan.FailNode("A", cascadeSkip: false);

        Assert.Equal(NodeExecutionStatus.Failed, plan.NodeStates["A"].Status);
        Assert.Equal(NodeExecutionStatus.Pending, plan.NodeStates["B"].Status);
    }

    // ────────────────── IsComplete / HasFailed ──────────────────

    [Fact]
    public void IsComplete_AllDone_ReturnsTrue()
    {
        var wf = Workflow(
            [Node("A"), Node("B")],
            [Edge("A", "B")]);

        var plan = ExecutionPlan.Build(wf);
        plan.NodeStates["A"].Status = NodeExecutionStatus.Completed;
        plan.NodeStates["B"].Status = NodeExecutionStatus.Completed;

        Assert.True(plan.IsComplete());
        Assert.False(plan.HasFailed());
    }

    [Fact]
    public void IsComplete_MixedTerminalStates_ReturnsTrue()
    {
        var wf = Workflow(
            [Node("A"), Node("B"), Node("C")],
            [Edge("A", "B"), Edge("A", "C")]);

        var plan = ExecutionPlan.Build(wf);
        plan.NodeStates["A"].Status = NodeExecutionStatus.Completed;
        plan.NodeStates["B"].Status = NodeExecutionStatus.Failed;
        plan.NodeStates["C"].Status = NodeExecutionStatus.Skipped;

        Assert.True(plan.IsComplete());
        Assert.True(plan.HasFailed());
    }

    [Fact]
    public void IsComplete_StillPending_ReturnsFalse()
    {
        var wf = Workflow(
            [Node("A"), Node("B")],
            [Edge("A", "B")]);

        var plan = ExecutionPlan.Build(wf);
        plan.NodeStates["A"].Status = NodeExecutionStatus.Completed;
        // B still Pending

        Assert.False(plan.IsComplete());
    }

    // ────────────────── Topological order properties ──────────────────

    [Fact]
    public void TopologicalOrder_ContainsAllNodes()
    {
        var wf = Workflow(
            [Node("X"), Node("Y"), Node("Z")],
            [Edge("X", "Y"), Edge("Y", "Z")]);

        var plan = ExecutionPlan.Build(wf);

        Assert.Equal(3, plan.TopologicalOrder.Count);
        Assert.Contains("X", plan.TopologicalOrder);
        Assert.Contains("Y", plan.TopologicalOrder);
        Assert.Contains("Z", plan.TopologicalOrder);
    }

    [Fact]
    public void TopologicalOrder_DisconnectedNodes_AllIncluded()
    {
        // Three isolated nodes with no edges
        var wf = Workflow([Node("A"), Node("B"), Node("C")], []);

        var plan = ExecutionPlan.Build(wf);

        Assert.True(plan.IsValid);
        Assert.Equal(3, plan.TopologicalOrder.Count);
        Assert.Equal(3, plan.GetReadyNodes().Count());
    }

    [Fact]
    public void TopologicalOrder_WideGraph_RespectsAllEdges()
    {
        //   A
        //  /|\
        // B C D
        //  \|/
        //   E
        var wf = Workflow(
            [Node("A"), Node("B"), Node("C"), Node("D"), Node("E")],
            [
                Edge("A", "B"), Edge("A", "C"), Edge("A", "D"),
                Edge("B", "E"), Edge("C", "E"), Edge("D", "E")
            ]);

        var plan = ExecutionPlan.Build(wf);

        Assert.True(plan.IsValid);
        var idx = plan.TopologicalOrder.ToDictionary(id => id, id => plan.TopologicalOrder.IndexOf(id));

        Assert.True(idx["A"] < idx["B"]);
        Assert.True(idx["A"] < idx["C"]);
        Assert.True(idx["A"] < idx["D"]);
        Assert.True(idx["B"] < idx["E"]);
        Assert.True(idx["C"] < idx["E"]);
        Assert.True(idx["D"] < idx["E"]);
    }
}