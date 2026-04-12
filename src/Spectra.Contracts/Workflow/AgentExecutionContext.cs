namespace Spectra.Contracts.Workflow;

/// <summary>
/// Internal tracking object threaded through handoff and delegation chains.
/// Carries guard-rail state, budget accounting, and lineage information.
/// Injected into StepContext.Inputs under the reserved key <c>__agentExecutionContext</c>.
/// </summary>
public class AgentExecutionContext
{
    /// <summary>Current depth in the handoff chain (incremented on each handoff).</summary>
    public int ChainDepth { get; set; }

    /// <summary>Current depth in the delegation stack (incremented on each supervisor delegation).</summary>
    public int DelegationDepth { get; set; }

    /// <summary>Total tokens consumed across the entire handoff/delegation chain.</summary>
    public int TotalTokensConsumed { get; set; }

    /// <summary>Remaining global token budget. 0 = unlimited.</summary>
    public int GlobalBudgetRemaining { get; set; }

    /// <summary>History of handoffs for audit trail.</summary>
    public List<AgentHandoffRecord> HandoffHistory { get; set; } = [];

    /// <summary>Agents already visited in the current handoff chain (for cycle detection).</summary>
    public HashSet<string> VisitedAgents { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Absolute wall-clock deadline for the entire multi-agent session.</summary>
    public DateTimeOffset? WallClockDeadline { get; set; }

    /// <summary>Cycle policy governing agent revisits.</summary>
    public CyclePolicy CyclePolicy { get; set; } = CyclePolicy.Deny;

    /// <summary>The originating run ID (stable across handoffs).</summary>
    public string OriginatorRunId { get; set; } = string.Empty;

    /// <summary>The agent that initiated the current handoff/delegation (null for the first agent).</summary>
    public string? ParentAgentId { get; set; }

    /// <summary>
    /// Creates a deep copy suitable for passing into a child agent (handoff or delegation).
    /// </summary>
    public AgentExecutionContext Fork()
    {
        return new AgentExecutionContext
        {
            ChainDepth = ChainDepth,
            DelegationDepth = DelegationDepth,
            TotalTokensConsumed = TotalTokensConsumed,
            GlobalBudgetRemaining = GlobalBudgetRemaining,
            HandoffHistory = [.. HandoffHistory],
            VisitedAgents = new HashSet<string>(VisitedAgents, StringComparer.OrdinalIgnoreCase),
            WallClockDeadline = WallClockDeadline,
            CyclePolicy = CyclePolicy,
            OriginatorRunId = OriginatorRunId,
            ParentAgentId = ParentAgentId
        };
    }
}

/// <summary>
/// A single record in the handoff audit trail.
/// </summary>
public class AgentHandoffRecord
{
    public required string FromAgent { get; init; }
    public required string ToAgent { get; init; }
    public required string Intent { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public int ChainDepth { get; init; }
    public int TokensConsumedAtHandoff { get; init; }
}