using Spectra.Contracts.Workflow;

namespace Spectra.Kernel.Scheduling;

public class ExecutionPlan
{
    public Dictionary<string, NodeExecutionState> NodeStates { get; } = [];
    public Dictionary<string, List<string>> Dependencies { get; } = [];
    public Dictionary<string, List<string>> Dependents { get; } = [];
    public string? EntryNodeId { get; init; }

    /// <summary>
    /// Topologically sorted node IDs. Nodes appear after all their dependencies.
    /// Only contains non-loopback relationships. Empty if the graph has cycles
    /// (outside of declared loopback edges).
    /// </summary>
    public List<string> TopologicalOrder { get; private set; } = [];

    /// <summary>
    /// Validation errors found during build. Empty means the plan is valid.
    /// </summary>
    public List<string> ValidationErrors { get; } = [];

    /// <summary>
    /// Builds an execution plan from a workflow definition.
    /// Performs dependency resolution, topological sorting, and cycle detection.
    /// Loopback edges (IsLoopback = true) are tracked but excluded from
    /// topological sort and cycle detection.
    /// </summary>
    public static ExecutionPlan Build(WorkflowDefinition workflow)
    {
        var plan = new ExecutionPlan { EntryNodeId = workflow.EntryNodeId };

        if (workflow.Nodes.Count == 0)
        {
            plan.ValidationErrors.Add("Workflow has no nodes.");
            return plan;
        }

        var nodeIds = new HashSet<string>();

        // Initialize all nodes
        foreach (var node in workflow.Nodes)
        {
            if (!nodeIds.Add(node.Id))
            {
                plan.ValidationErrors.Add($"Duplicate node ID: '{node.Id}'.");
                continue;
            }

            plan.NodeStates[node.Id] = new NodeExecutionState { NodeId = node.Id };
            plan.Dependencies[node.Id] = [];
            plan.Dependents[node.Id] = [];
        }

        // Validate entry node reference
        if (!string.IsNullOrEmpty(workflow.EntryNodeId) && !nodeIds.Contains(workflow.EntryNodeId))
        {
            plan.ValidationErrors.Add($"EntryNodeId '{workflow.EntryNodeId}' does not match any node.");
        }

        // Build dependency graph from edges (excluding loopbacks for ordering)
        foreach (var edge in workflow.Edges)
        {
            if (!nodeIds.Contains(edge.From))
            {
                plan.ValidationErrors.Add($"Edge references unknown source node: '{edge.From}'.");
                continue;
            }
            if (!nodeIds.Contains(edge.To))
            {
                plan.ValidationErrors.Add($"Edge references unknown target node: '{edge.To}'.");
                continue;
            }

            // Loopback edges are valid for cyclic workflows but must not
            // participate in topological sort or cycle detection.
            if (edge.IsLoopback)
                continue;

            plan.Dependencies[edge.To].Add(edge.From);
            plan.Dependents[edge.From].Add(edge.To);
        }

        // Set total dependencies count
        foreach (var node in workflow.Nodes)
        {
            if (plan.NodeStates.ContainsKey(node.Id))
            {
                plan.NodeStates[node.Id].TotalDependencies = plan.Dependencies[node.Id].Count;
            }
        }

        // Topological sort via Kahn's algorithm (also detects cycles)
        plan.TopologicalOrder = TopologicalSort(plan, out var hasCycle);
        if (hasCycle)
        {
            plan.ValidationErrors.Add("Workflow contains a cycle among non-loopback edges.");
        }

        // Mark entry node(s) as ready
        if (!string.IsNullOrEmpty(workflow.EntryNodeId) && plan.NodeStates.ContainsKey(workflow.EntryNodeId))
        {
            plan.NodeStates[workflow.EntryNodeId].Status = NodeExecutionStatus.Ready;
        }
        else
        {
            // All nodes with zero dependencies are entry points
            foreach (var id in nodeIds)
            {
                if (plan.Dependencies[id].Count == 0)
                {
                    plan.NodeStates[id].Status = NodeExecutionStatus.Ready;
                }
            }
        }

        return plan;
    }

    /// <summary>
    /// Returns the IDs of all nodes currently in Ready status.
    /// </summary>
    public IEnumerable<string> GetReadyNodes()
    {
        return NodeStates
            .Where(kv => kv.Value.Status == NodeExecutionStatus.Ready)
            .Select(kv => kv.Key);
    }

    /// <summary>
    /// Marks a node as completed and transitions its dependents to Ready
    /// when all their dependencies are satisfied.
    /// </summary>
    /// <param name="nodeId">The completed node ID.</param>
    /// <param name="nodeDefinitions">Node definitions for WaitForAll lookup.</param>
    public void CompleteNode(string nodeId, IReadOnlyDictionary<string, NodeDefinition>? nodeDefinitions = null)
    {
        if (!NodeStates.TryGetValue(nodeId, out var nodeState))
            return;

        nodeState.Status = NodeExecutionStatus.Completed;
        nodeState.CompletedAt = DateTimeOffset.UtcNow;

        if (!Dependents.TryGetValue(nodeId, out var dependents))
            return;

        foreach (var dependentId in dependents)
        {
            if (!NodeStates.TryGetValue(dependentId, out var depState))
                continue;

            // Skip nodes that are already running, completed, failed, or skipped
            if (depState.Status is not (NodeExecutionStatus.Pending or NodeExecutionStatus.Ready))
                continue;

            depState.CompletedDependencies.Add(nodeId);

            var waitForAll = true;
            if (nodeDefinitions != null && nodeDefinitions.TryGetValue(dependentId, out var depNode))
            {
                waitForAll = depNode.WaitForAll;
            }

            if (waitForAll)
            {
                // All dependencies must be completed
                if (depState.CompletedDependencies.Count >= depState.TotalDependencies)
                {
                    depState.Status = NodeExecutionStatus.Ready;
                }
            }
            else
            {
                // Any single dependency completing is enough
                depState.Status = NodeExecutionStatus.Ready;
            }
        }
    }

    /// <summary>
    /// Marks a node as failed. Optionally cascades failure to all
    /// downstream dependents by marking them as Skipped.
    /// </summary>
    public void FailNode(string nodeId, bool cascadeSkip = false)
    {
        if (!NodeStates.TryGetValue(nodeId, out var nodeState))
            return;

        nodeState.Status = NodeExecutionStatus.Failed;
        nodeState.CompletedAt = DateTimeOffset.UtcNow;

        if (cascadeSkip)
        {
            SkipDownstream(nodeId);
        }
    }

    /// <summary>
    /// Returns true when every node is in a terminal state (Completed, Failed, or Skipped).
    /// </summary>
    public bool IsComplete()
    {
        return NodeStates.Values.All(s =>
            s.Status is NodeExecutionStatus.Completed
                or NodeExecutionStatus.Failed
                or NodeExecutionStatus.Skipped
                or NodeExecutionStatus.Interrupted);
    }

    /// <summary>
    /// Returns true if any node has failed.
    /// </summary>
    public bool HasFailed()
    {
        return NodeStates.Values.Any(s => s.Status == NodeExecutionStatus.Failed);
    }

    /// <summary>
    /// Returns true if the plan was built without validation errors.
    /// </summary>
    public bool IsValid => ValidationErrors.Count == 0;

    // ────────────────── Private helpers ──────────────────

    /// <summary>
    /// Kahn's algorithm: iteratively removes nodes with zero in-degree.
    /// If the resulting order contains fewer nodes than the graph, there is a cycle.
    /// </summary>
    private static List<string> TopologicalSort(ExecutionPlan plan, out bool hasCycle)
    {
        var inDegree = new Dictionary<string, int>();
        foreach (var (id, deps) in plan.Dependencies)
        {
            inDegree[id] = deps.Count;
        }

        var queue = new Queue<string>();
        foreach (var (id, degree) in inDegree)
        {
            if (degree == 0)
                queue.Enqueue(id);
        }

        var sorted = new List<string>(plan.NodeStates.Count);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            sorted.Add(current);

            if (!plan.Dependents.TryGetValue(current, out var dependents))
                continue;

            foreach (var dep in dependents)
            {
                inDegree[dep]--;
                if (inDegree[dep] == 0)
                    queue.Enqueue(dep);
            }
        }

        hasCycle = sorted.Count < plan.NodeStates.Count;
        return sorted;
    }

    /// <summary>
    /// Recursively marks all downstream dependents as Skipped.
    /// </summary>
    private void SkipDownstream(string nodeId)
    {
        if (!Dependents.TryGetValue(nodeId, out var dependents))
            return;

        foreach (var depId in dependents)
        {
            if (!NodeStates.TryGetValue(depId, out var depState))
                continue;

            if (depState.Status is NodeExecutionStatus.Pending or NodeExecutionStatus.Ready)
            {
                depState.Status = NodeExecutionStatus.Skipped;
                depState.CompletedAt = DateTimeOffset.UtcNow;
                SkipDownstream(depId);
            }
        }
    }
}