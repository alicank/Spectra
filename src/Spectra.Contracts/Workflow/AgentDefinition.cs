using System.Text.Json.Serialization;
namespace Spectra.Contracts.Workflow;

public class AgentDefinition
{
    public required string Id { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }

    public string? ApiKeyEnvVar { get; init; }
    public string? ApiVersionOverride { get; init; }


    // Default parameters
    public double Temperature { get; init; } = 0.7;
    public int MaxTokens { get; init; } = 2048;

    // System prompt - can be inline OR reference a prompt file
    public string? SystemPrompt { get; init; }           // Inline prompt
    public string? SystemPromptRef { get; init; }        // Reference to prompt ID (e.g., "agents/coder")

    // Credentials
    public string? ApiKeyRef { get; init; }
    public string? BaseUrlOverride { get; init; }

    // Alternative models for fallback
    // ── Multi-agent handoff & delegation ──

    /// <summary>
    /// Agent IDs that this agent is allowed to hand off to via <c>transfer_to_agent</c>.
    /// Empty means handoffs are disabled for this agent.
    /// </summary>
    public List<string> HandoffTargets { get; init; } = [];

    /// <summary>
    /// Controls whether handoffs require approval before executing.
    /// </summary>
    public HandoffPolicy HandoffPolicy { get; init; } = HandoffPolicy.Allowed;

    /// <summary>
    /// Agent IDs that this agent can delegate to as a supervisor.
    /// When non-empty, a <c>delegate_to_agent</c> tool is auto-injected.
    /// </summary>
    public List<string> SupervisorWorkers { get; init; } = [];

    /// <summary>
    /// Controls whether delegations require approval before executing.
    /// </summary>
    public DelegationPolicy DelegationPolicy { get; init; } = DelegationPolicy.Allowed;

    /// <summary>
    /// Maximum depth of nested delegations (supervisor → worker → sub-worker).
    /// </summary>
    public int MaxDelegationDepth { get; init; } = 3;

    /// <summary>
    /// Maximum depth of handoff chains (A → B → C → ...).
    /// </summary>
    public int MaxHandoffChainDepth { get; init; } = 5;

    /// <summary>
    /// How much conversation context to transfer on handoff.
    /// </summary>
    public ConversationScope ConversationScope { get; init; } = ConversationScope.Handoff;

    /// <summary>
    /// When <see cref="ConversationScope"/> is <see cref="Workflow.ConversationScope.LastN"/>,
    /// the number of messages to transfer. Default is 10.
    /// </summary>
    public int MaxContextMessages { get; init; } = 10;

    /// <summary>
    /// Cycle policy governing whether this agent can be revisited in a handoff chain.
    /// </summary>
    public CyclePolicy CyclePolicy { get; init; } = CyclePolicy.Deny;

    /// <summary>
    /// Agent ID to escalate to when this agent fails, exceeds budget, or hits max iterations.
    /// Use "human" (reserved) to trigger an interrupt for human escalation.
    /// </summary>
    public string? EscalationTarget { get; init; }

    /// <summary>
    /// Per-agent wall-clock timeout. Overrides the workflow-level default when set.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// State paths this agent is allowed to read. Empty = unrestricted.
    /// Supports wildcards, e.g. "Context.*", "Inputs.task".
    /// </summary>
    public List<string> StateReadPaths { get; init; } = [];

    /// <summary>
    /// State paths this agent is allowed to write. Empty = unrestricted.
    /// </summary>
    public List<string> StateWritePaths { get; init; } = [];

    /// <summary>
    /// Tool names this agent is allowed to use. Empty = no tools (tools must be explicitly listed).
    /// </summary>
    public List<string> Tools { get; init; } = [];

    [JsonPropertyName("alternativeModels")]
    public List<string> AlternativeModels { get; init; } = new();
}