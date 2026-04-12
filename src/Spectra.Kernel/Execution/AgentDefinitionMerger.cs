using Spectra.Contracts.Workflow;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Merges agent definitions across three layers: global (registry) → workflow-level → runtime overrides.
/// Non-null/non-default fields at each layer win over the layer below,
/// similar to how appsettings.json layering works in .NET.
/// </summary>
public static class AgentDefinitionMerger
{
    /// <summary>
    /// Merges a base agent definition with an overlay. Non-null/non-default
    /// fields in <paramref name="overlay"/> override <paramref name="baseAgent"/>.
    /// </summary>
    public static AgentDefinition Merge(AgentDefinition baseAgent, AgentDefinition overlay)
    {
        ArgumentNullException.ThrowIfNull(baseAgent);
        ArgumentNullException.ThrowIfNull(overlay);

        return new AgentDefinition
        {
            Id = baseAgent.Id,
            Provider = !string.IsNullOrEmpty(overlay.Provider) ? overlay.Provider : baseAgent.Provider,
            Model = !string.IsNullOrEmpty(overlay.Model) ? overlay.Model : baseAgent.Model,
            ApiKeyEnvVar = overlay.ApiKeyEnvVar ?? baseAgent.ApiKeyEnvVar,
            ApiVersionOverride = overlay.ApiVersionOverride ?? baseAgent.ApiVersionOverride,
            Temperature = overlay.Temperature != 0.7 ? overlay.Temperature : baseAgent.Temperature,
            MaxTokens = overlay.MaxTokens != 2048 ? overlay.MaxTokens : baseAgent.MaxTokens,
            SystemPrompt = overlay.SystemPrompt ?? baseAgent.SystemPrompt,
            SystemPromptRef = overlay.SystemPromptRef ?? baseAgent.SystemPromptRef,
            ApiKeyRef = overlay.ApiKeyRef ?? baseAgent.ApiKeyRef,
            BaseUrlOverride = overlay.BaseUrlOverride ?? baseAgent.BaseUrlOverride,
            HandoffTargets = overlay.HandoffTargets.Count > 0 ? overlay.HandoffTargets : baseAgent.HandoffTargets,
            HandoffPolicy = overlay.HandoffPolicy != HandoffPolicy.Allowed ? overlay.HandoffPolicy : baseAgent.HandoffPolicy,
            SupervisorWorkers = overlay.SupervisorWorkers.Count > 0 ? overlay.SupervisorWorkers : baseAgent.SupervisorWorkers,
            DelegationPolicy = overlay.DelegationPolicy != DelegationPolicy.Allowed ? overlay.DelegationPolicy : baseAgent.DelegationPolicy,
            MaxDelegationDepth = overlay.MaxDelegationDepth != 3 ? overlay.MaxDelegationDepth : baseAgent.MaxDelegationDepth,
            MaxHandoffChainDepth = overlay.MaxHandoffChainDepth != 5 ? overlay.MaxHandoffChainDepth : baseAgent.MaxHandoffChainDepth,
            ConversationScope = overlay.ConversationScope != ConversationScope.Handoff ? overlay.ConversationScope : baseAgent.ConversationScope,
            MaxContextMessages = overlay.MaxContextMessages != 10 ? overlay.MaxContextMessages : baseAgent.MaxContextMessages,
            CyclePolicy = overlay.CyclePolicy != CyclePolicy.Deny ? overlay.CyclePolicy : baseAgent.CyclePolicy,
            EscalationTarget = overlay.EscalationTarget ?? baseAgent.EscalationTarget,
            Timeout = overlay.Timeout ?? baseAgent.Timeout,
            StateReadPaths = overlay.StateReadPaths.Count > 0 ? overlay.StateReadPaths : baseAgent.StateReadPaths,
            StateWritePaths = overlay.StateWritePaths.Count > 0 ? overlay.StateWritePaths : baseAgent.StateWritePaths,
            AlternativeModels = overlay.AlternativeModels.Count > 0 ? overlay.AlternativeModels : baseAgent.AlternativeModels,
            Tools = overlay.Tools.Count > 0 ? overlay.Tools : baseAgent.Tools,
        };
    }

    /// <summary>
    /// Resolves an agent definition by merging three layers:
    /// global (from <paramref name="globalAgent"/>), workflow-level (from <paramref name="workflowAgent"/>),
    /// and runtime overrides (from <paramref name="runtimeOverride"/>).
    /// Any layer may be null, in which case it is skipped.
    /// </summary>
    public static AgentDefinition Resolve(
        AgentDefinition? globalAgent,
        AgentDefinition? workflowAgent,
        AgentDefinition? runtimeOverride)
    {
        var result = globalAgent;

        if (result is null && workflowAgent is null && runtimeOverride is null)
            throw new InvalidOperationException("At least one agent definition layer must be provided.");

        if (result is null)
            result = workflowAgent ?? runtimeOverride!;
        else if (workflowAgent is not null)
            result = Merge(result, workflowAgent);

        if (runtimeOverride is not null && result is not null)
            result = Merge(result, runtimeOverride);

        return result!;
    }
}