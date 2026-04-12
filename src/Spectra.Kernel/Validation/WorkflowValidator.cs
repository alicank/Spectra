using Spectra.Contracts.Validation;
using Spectra.Contracts.Workflow;

namespace Spectra.Kernel.Validation;

/// <summary>
/// Validates a <see cref="WorkflowDefinition"/> for structural correctness
/// before execution. Catches authoring errors that would otherwise cause
/// silent hangs, circular dependencies, or unreachable nodes.
/// </summary>
public static class WorkflowValidator
{
    /// <summary>
    /// Performs comprehensive structural validation on a workflow definition.
    /// Returns errors for fatal issues and warnings for potential problems.
    /// </summary>
    public static ValidationResult Validate(WorkflowDefinition workflow)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (workflow.Nodes.Count == 0)
        {
            errors.Add("Workflow has no nodes.");
            return ValidationResult.FailWithDetails(errors);
        }

        var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Duplicate node IDs ──
        foreach (var node in workflow.Nodes)
        {
            if (!nodeIds.Add(node.Id))
                errors.Add($"Duplicate node ID: '{node.Id}'.");
        }

        // ── Entry node validation ──
        var entryNodeId = workflow.EntryNodeId ?? workflow.Nodes.FirstOrDefault()?.Id;
        if (!string.IsNullOrEmpty(workflow.EntryNodeId) && !nodeIds.Contains(workflow.EntryNodeId))
            errors.Add($"EntryNodeId '{workflow.EntryNodeId}' does not match any node.");

        // ── Edge reference validation ──
        foreach (var edge in workflow.Edges)
        {
            if (!nodeIds.Contains(edge.From))
                errors.Add($"Edge references unknown source node: '{edge.From}'.");
            if (!nodeIds.Contains(edge.To))
                errors.Add($"Edge references unknown target node: '{edge.To}'.");
            if (edge.From == edge.To && !edge.IsLoopback)
                errors.Add($"Edge from '{edge.From}' to itself must be marked as loopback.");
        }

        // ── Cycle detection (non-loopback edges only) ──
        var nonLoopbackEdges = workflow.Edges.Where(e => !e.IsLoopback).ToList();
        if (HasCycle(nodeIds, nonLoopbackEdges))
            errors.Add("Workflow contains a cycle among non-loopback edges. Mark intentional cycles with 'isLoopback: true'.");

        // ── Unreachable node detection ──
        if (!string.IsNullOrEmpty(entryNodeId) && nodeIds.Contains(entryNodeId))
        {
            var reachable = FindReachableNodes(entryNodeId, workflow.Edges, nodeIds);
            var unreachable = nodeIds.Where(id => !reachable.Contains(id)).ToList();
            foreach (var id in unreachable)
                warnings.Add($"Node '{id}' is unreachable from entry node '{entryNodeId}'.");
        }

        // ── Fan-out detection (sequential runner incompatibility) ──
        var fanOutSources = DetectFanOut(workflow);
        foreach (var source in fanOutSources)
        {
            var targets = workflow.Edges
                .Where(e => e.From == source && !e.IsLoopback && string.IsNullOrEmpty(e.Condition))
                .Select(e => e.To)
                .ToList();

            warnings.Add(
                $"Node '{source}' fans out to [{string.Join(", ", targets)}]. " +
                "The sequential WorkflowRunner follows only the first unconditional edge; " +
                "use ParallelScheduler or make the paths sequential to avoid hanging.");
        }

        // ── WaitForAll / merge node validation ──
        foreach (var node in workflow.Nodes.Where(n => n.WaitForAll))
        {
            var incomingCount = workflow.Edges.Count(e => e.To == node.Id && !e.IsLoopback);
            if (incomingCount < 2)
            {
                warnings.Add(
                    $"Node '{node.Id}' has waitForAll=true but only {incomingCount} incoming edge(s). " +
                    "waitForAll is meaningful only with 2+ incoming edges.");
            }
        }

        // ── Merge step source validation ──
        foreach (var node in workflow.Nodes.Where(n =>
            n.StepType.Equals("merge_results", StringComparison.OrdinalIgnoreCase)))
        {
            if (node.Parameters.TryGetValue("sources", out var sourcesObj) && sourcesObj is IEnumerable<object> sources)
            {
                foreach (var s in sources)
                {
                    var sourceId = s?.ToString();
                    if (!string.IsNullOrEmpty(sourceId) && !nodeIds.Contains(sourceId))
                        errors.Add($"merge_results node '{node.Id}' references unknown source node: '{sourceId}'.");
                }
            }
        }

        // ── Subgraph validation ──
        ValidateSubgraphs(workflow, nodeIds, errors, warnings);

        // ── Agent reference validation ──
        var agentIds = new HashSet<string>(workflow.Agents.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var node in workflow.Nodes.Where(n => !string.IsNullOrEmpty(n.AgentId)))
        {
            if (!agentIds.Contains(node.AgentId!))
                errors.Add($"Node '{node.Id}' references undefined agent '{node.AgentId}'.");
        }

        if (errors.Count > 0)
            return ValidationResult.FailWithDetails(errors, warnings);

        if (warnings.Count > 0)
            return ValidationResult.SuccessWithWarnings(warnings);

        return ValidationResult.Success();
    }

    // ────────────────── Subgraph validation ──────────────────

    private static void ValidateSubgraphs(
        WorkflowDefinition workflow,
        HashSet<string> nodeIds,
        List<string> errors,
        List<string> warnings)
    {
        var subgraphIds = new HashSet<string>(
            workflow.Subgraphs.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);

        // Check subgraph nodes reference valid subgraphs
        foreach (var node in workflow.Nodes)
        {
            var subgraphId = node.SubgraphId;
            if (string.IsNullOrEmpty(subgraphId) &&
                node.Parameters.TryGetValue("__subgraphId", out var sid))
            {
                subgraphId = sid?.ToString();
            }

            if (!string.IsNullOrEmpty(subgraphId) && !subgraphIds.Contains(subgraphId))
                errors.Add($"Node '{node.Id}' references undefined subgraph '{subgraphId}'.");
        }

        // Recursive subgraph detection
        foreach (var subgraph in workflow.Subgraphs)
        {
            if (DetectSubgraphSelfReference(workflow.Id, subgraph, workflow.Subgraphs, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            {
                errors.Add(
                    $"Subgraph '{subgraph.Id}' creates a circular dependency " +
                    $"(directly or transitively references parent workflow '{workflow.Id}').");
            }

            // Validate child workflow structure recursively
            if (subgraph.Workflow != null)
            {
                var childResult = Validate(subgraph.Workflow);
                foreach (var err in childResult.Errors)
                    errors.Add($"Subgraph '{subgraph.Id}': {err}");
                foreach (var warn in childResult.Warnings)
                    warnings.Add($"Subgraph '{subgraph.Id}': {warn}");
            }
        }
    }

    /// <summary>
    /// Detects if a subgraph (directly or transitively) references the parent workflow,
    /// which would create a circular dependency causing infinite recursion or a hang.
    /// </summary>
    private static bool DetectSubgraphSelfReference(
        string parentWorkflowId,
        SubgraphDefinition subgraph,
        List<SubgraphDefinition> allSubgraphs,
        HashSet<string> visited)
    {
        if (subgraph.Workflow == null)
            return false;

        if (subgraph.Workflow.Id.Equals(parentWorkflowId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!visited.Add(subgraph.Id))
            return false; // Already checked this path

        // Check child workflow's subgraphs transitively
        foreach (var childSubgraph in subgraph.Workflow.Subgraphs)
        {
            if (DetectSubgraphSelfReference(parentWorkflowId, childSubgraph, allSubgraphs, visited))
                return true;
        }

        // Check if child workflow references any subgraph from parent that we haven't visited
        foreach (var node in subgraph.Workflow.Nodes)
        {
            var refId = node.SubgraphId;
            if (string.IsNullOrEmpty(refId) &&
                node.Parameters.TryGetValue("__subgraphId", out var sid))
            {
                refId = sid?.ToString();
            }

            if (string.IsNullOrEmpty(refId)) continue;

            var referencedSubgraph = allSubgraphs.FirstOrDefault(s =>
                s.Id.Equals(refId, StringComparison.OrdinalIgnoreCase));

            if (referencedSubgraph != null &&
                DetectSubgraphSelfReference(parentWorkflowId, referencedSubgraph, allSubgraphs, visited))
            {
                return true;
            }
        }

        return false;
    }

    // ────────────────── Graph algorithms ──────────────────

    /// <summary>
    /// Kahn's algorithm cycle detection on non-loopback edges.
    /// </summary>
    private static bool HasCycle(HashSet<string> nodeIds, List<EdgeDefinition> edges)
    {
        var inDegree = nodeIds.ToDictionary(id => id, _ => 0, StringComparer.OrdinalIgnoreCase);
        var adjacency = nodeIds.ToDictionary(id => id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges)
        {
            if (!inDegree.ContainsKey(edge.From) || !inDegree.ContainsKey(edge.To))
                continue;

            adjacency[edge.From].Add(edge.To);
            inDegree[edge.To]++;
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var visited = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visited++;

            foreach (var dep in adjacency[current])
            {
                inDegree[dep]--;
                if (inDegree[dep] == 0)
                    queue.Enqueue(dep);
            }
        }

        return visited < nodeIds.Count;
    }

    /// <summary>
    /// BFS/DFS to find all nodes reachable from the entry node.
    /// </summary>
    private static HashSet<string> FindReachableNodes(
        string entryNodeId,
        List<EdgeDefinition> edges,
        HashSet<string> allNodeIds)
    {
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entryNodeId };
        var queue = new Queue<string>();
        queue.Enqueue(entryNodeId);

        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in allNodeIds)
            adjacency[id] = [];

        foreach (var edge in edges)
        {
            if (adjacency.ContainsKey(edge.From))
                adjacency[edge.From].Add(edge.To);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current, out var neighbors)) continue;

            foreach (var neighbor in neighbors)
            {
                if (reachable.Add(neighbor))
                    queue.Enqueue(neighbor);
            }
        }

        return reachable;
    }

    /// <summary>
    /// Detects nodes with multiple unconditional, non-loopback outgoing edges (fan-out).
    /// </summary>
    private static List<string> DetectFanOut(WorkflowDefinition workflow)
    {
        return workflow.Nodes
            .Where(node =>
            {
                var unconditionalOutgoing = workflow.Edges
                    .Where(e => e.From == node.Id && !e.IsLoopback && string.IsNullOrEmpty(e.Condition))
                    .ToList();
                return unconditionalOutgoing.Count > 1;
            })
            .Select(n => n.Id)
            .ToList();
    }
}