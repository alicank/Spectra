namespace Spectra.Contracts.Workflow;

public class NodeDefinition
{
    public required string Id { get; set; }
    public required string StepType { get; set; }
    public string? AgentId { get; set; }
    public Dictionary<string, object?> Parameters { get; set; } = [];

    /// <summary>
    /// Reference to a prompt file ID for the user prompt.
    /// Resolved through IPromptRegistry and rendered with PromptRenderer,
    /// following the same pattern as agent SystemPromptRef.
    /// </summary>
    public string? UserPromptRef { get; set; }

    public Dictionary<string, string> InputMappings { get; set; } = [];
    public Dictionary<string, string> OutputMappings { get; set; } = [];

    /// <summary>
    /// If true, this node waits for ALL incoming edges to complete before executing (join/barrier).
    /// If false (default), executes when ANY incoming edge completes.
    /// </summary>
    public bool WaitForAll { get; set; } = false;

    /// <summary>
    /// When set, the runner automatically pauses BEFORE executing this node,
    /// using this value as the interrupt reason.
    /// </summary>
    public string? InterruptBefore { get; set; }

    /// <summary>
    /// When set, the runner automatically pauses AFTER executing this node,
    /// using this value as the interrupt reason.
    /// </summary>
    public string? InterruptAfter { get; set; }

    /// <summary>
    /// When set, this node executes the referenced subgraph (child workflow)
    /// with isolated state. The node's StepType should be "subgraph".
    /// </summary>
    public string? SubgraphId { get; set; }

    /// <summary>
    /// Agent IDs this node's agent can hand off to (populated from builder).
    /// </summary>
    public List<string> HandoffTargets { get; set; } = [];
}