using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Helpers for reading and writing <see cref="AgentExecutionContext"/> from
/// step inputs and workflow state.
/// </summary>
public static class AgentExecutionContextHelper
{
    public const string ContextKey = "__agentExecutionContext";

    /// <summary>
    /// Retrieves the execution context from step inputs, or null if not present.
    /// </summary>
    public static AgentExecutionContext? GetFromInputs(StepContext context)
    {
        return context.Inputs.TryGetValue(ContextKey, out var val)
            ? val as AgentExecutionContext
            : null;
    }

    /// <summary>
    /// Retrieves the execution context from workflow state context, or null if not present.
    /// </summary>
    public static AgentExecutionContext? GetFromState(WorkflowState state)
    {
        return state.Context.TryGetValue(ContextKey, out var val)
            ? val as AgentExecutionContext
            : null;
    }

    /// <summary>
    /// Stores the execution context into workflow state context for child access.
    /// </summary>
    public static void StoreInState(WorkflowState state, AgentExecutionContext execCtx)
    {
        state.Context[ContextKey] = execCtx;
    }

    /// <summary>
    /// Gets or creates an execution context from step inputs, initializing
    /// from workflow-level and agent-level configuration.
    /// </summary>
    public static AgentExecutionContext GetOrCreate(
        StepContext context,
        AgentDefinition? agent,
        WorkflowDefinition? workflow)
    {
        var existing = GetFromInputs(context);
        if (existing is not null)
            return existing;

        var execCtx = new AgentExecutionContext
        {
            OriginatorRunId = context.RunId,
            CyclePolicy = agent?.CyclePolicy ?? CyclePolicy.Deny,
            GlobalBudgetRemaining = workflow?.GlobalTokenBudget ?? 0
        };

        if (agent?.Timeout is not null)
        {
            execCtx.WallClockDeadline = DateTimeOffset.UtcNow + agent.Timeout.Value;
        }
        else if (workflow?.DefaultTimeout is { } wfTimeout && wfTimeout > TimeSpan.Zero)
        {
            execCtx.WallClockDeadline = DateTimeOffset.UtcNow + wfTimeout;
        }

        return execCtx;
    }

    /// <summary>
    /// Validates that a handoff to the target agent is permitted by all guard rails.
    /// Returns null if permitted, or an error message if blocked.
    /// </summary>
    public static string? ValidateHandoff(
        AgentExecutionContext execCtx,
        AgentDefinition sourceAgent,
        string targetAgentId,
        WorkflowDefinition? workflow)
    {
        // 1. Check if target is in allowed list
        if (!sourceAgent.HandoffTargets.Contains(targetAgentId, StringComparer.OrdinalIgnoreCase))
            return $"Agent '{sourceAgent.Id}' is not allowed to hand off to '{targetAgentId}'. " +
                   $"Allowed targets: {string.Join(", ", sourceAgent.HandoffTargets)}";

        // 2. Check handoff policy
        if (sourceAgent.HandoffPolicy == HandoffPolicy.Disabled)
            return $"Handoffs are disabled for agent '{sourceAgent.Id}'.";

        // 3. Check chain depth (agent-level)
        if (execCtx.ChainDepth >= sourceAgent.MaxHandoffChainDepth)
            return $"Maximum handoff chain depth ({sourceAgent.MaxHandoffChainDepth}) reached for agent '{sourceAgent.Id}'.";

        // 4. Check chain depth (workflow-level ceiling)
        if (workflow is not null && execCtx.ChainDepth >= workflow.MaxHandoffChainDepth)
            return $"Maximum workflow-level handoff chain depth ({workflow.MaxHandoffChainDepth}) reached.";

        // 5. Check cycle policy
        if (execCtx.VisitedAgents.Contains(targetAgentId))
        {
            switch (execCtx.CyclePolicy.Mode)
            {
                case CyclePolicyMode.Deny:
                    return $"Agent '{targetAgentId}' has already been visited in this handoff chain. " +
                           "Cycle policy is set to Deny.";

                case CyclePolicyMode.AllowWithLimit:
                    var visitCount = execCtx.HandoffHistory.Count(h =>
                        h.ToAgent.Equals(targetAgentId, StringComparison.OrdinalIgnoreCase));
                    if (visitCount >= execCtx.CyclePolicy.MaxRevisits)
                        return $"Agent '{targetAgentId}' has been visited {visitCount} times, " +
                               $"exceeding the max revisit limit of {execCtx.CyclePolicy.MaxRevisits}.";
                    break;

                case CyclePolicyMode.Allow:
                    break; // No restriction
            }
        }

        // 6. Check global budget
        if (execCtx.GlobalBudgetRemaining > 0 && execCtx.TotalTokensConsumed >= execCtx.GlobalBudgetRemaining)
            return "Global token budget exhausted. Cannot hand off.";

        // 7. Check wall-clock deadline
        if (execCtx.WallClockDeadline is not null && DateTimeOffset.UtcNow >= execCtx.WallClockDeadline)
            return "Wall-clock deadline exceeded. Cannot hand off.";

        return null; // All checks passed
    }
}