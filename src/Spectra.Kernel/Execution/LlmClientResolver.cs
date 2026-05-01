using System;
using System.Collections.Generic;
using System.Linq;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Prompts;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Contracts.Tools;
using Spectra.Contracts.Providers.Fallback;
using Spectra.Contracts.Events;
using Spectra.Kernel.Resilience;
using Spectra.Kernel.Prompts;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Shared utilities for resolving LLM clients, system prompts, and rendering
/// templates. Used by <see cref="PromptStep"/> and <see cref="AgenticLoopStep"/>
/// to avoid duplication of provider/agent resolution logic.
/// </summary>
internal static class LlmClientResolver
{
    /// <summary>
    /// Resolves an <see cref="ILlmClient"/> from the step context, either via
    /// an agentId lookup or direct provider+model specification.
    /// </summary>
    internal static (ILlmClient? Client, string? Error) ResolveClient(
        StepContext context,
        IAgentRegistry agentRegistry,
        IProviderRegistry providerRegistry)
    {
        var agentId = GetStringInput(context, "agentId");

        if (!string.IsNullOrEmpty(agentId))
        {
            var agent = TryGetAgent(context, agentRegistry);
            if (agent is null)
                return (null, $"Agent '{agentId}' not found in any registry layer (global, workflow, runtime).");

            var client = providerRegistry.CreateClient(agent);
            if (client is null)
                return (null, $"Provider '{agent.Provider}' could not create a client for agent '{agentId}'.");

            return (client, null);
        }

        var provider = GetStringInput(context, "provider");
        var model = GetStringInput(context, "model");

        if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(model))
            return (null, "Either 'agentId' or both 'provider' and 'model' must be specified.");

        var adHocAgent = new AgentDefinition
        {
            Id = $"_adhoc_{provider}_{model}",
            Provider = provider,
            Model = model,
            Temperature = GetDoubleInput(context, "temperature", 0.7),
            MaxTokens = GetIntInput(context, "maxTokens", 2048)
        };

        var adHocClient = providerRegistry.CreateClient(adHocAgent);
        if (adHocClient is null)
            return (null, $"Provider '{provider}' not found or could not create client for model '{model}'.");

        return (adHocClient, null);
    }

    /// <summary>
    /// Resolves an <see cref="ILlmClient"/> and wraps it with a <see cref="FallbackLlmClient"/>
    /// if the agent has a fallback policy configured via <see cref="AgentDefinition.AlternativeModels"/>
    /// or a named fallback policy is found in the registry.
    /// </summary>
    internal static (ILlmClient? Client, string? Error) ResolveClientWithFallback(
        StepContext context,
        IAgentRegistry agentRegistry,
        IProviderRegistry providerRegistry,
        IFallbackPolicyRegistry? fallbackPolicyRegistry,
        IEventSink? eventSink)
    {
        // First, resolve the primary client normally
        var (primaryClient, error) = ResolveClient(context, agentRegistry, providerRegistry);
        if (primaryClient is null)
            return (null, error);

        // Check if a fallback policy is specified in the step inputs
        var policyName = GetStringInput(context, "fallbackPolicy");
        if (string.IsNullOrEmpty(policyName))
        {
            // Check the agent definition for a fallback policy name
            var agent = TryGetAgent(context, agentRegistry);

            // If no policy name found, return the primary client as-is
            if (agent is null || agent.AlternativeModels.Count == 0)
                return (primaryClient, null);
        }

        if (fallbackPolicyRegistry is null)
            return (primaryClient, null);

        var policy = !string.IsNullOrEmpty(policyName)
            ? fallbackPolicyRegistry.GetPolicy(policyName)
            : null;

        if (policy is null)
            return (primaryClient, null);

        // Build fallback entries from the policy
        var entries = new List<FallbackClientEntry>();
        foreach (var entry in policy.Entries)
        {
            var entryAgent = new AgentDefinition
            {
                Id = $"_fallback_{entry.Provider}_{entry.Model}",
                Provider = entry.Provider,
                Model = entry.Model
            };

            var entryClient = providerRegistry.CreateClient(entryAgent);
            if (entryClient is null)
                continue;

            entries.Add(new FallbackClientEntry
            {
                Client = entryClient,
                Entry = entry
            });
        }

        if (entries.Count == 0)
            return (primaryClient, null);

        var fallbackClient = new FallbackLlmClient(
            policy,
            entries,
            eventSink,
            context.RunId,
            context.WorkflowId,
            context.NodeId);

        return (fallbackClient, null);
    }

    /// <summary>
    /// Resolves the <see cref="AgentDefinition"/> using the three-layer override chain:
    /// global (IAgentRegistry) → workflow-level (WorkflowDefinition.Agents) → runtime overrides.
    /// </summary>
    internal static AgentDefinition? TryGetAgent(StepContext context, IAgentRegistry agentRegistry)
    {
        var agentId = GetStringInput(context, "agentId");
        if (string.IsNullOrEmpty(agentId))
            return null;

        // Layer 1: Global agent from registry
        var globalAgent = agentRegistry.GetAgent(agentId);

        // Layer 2: Workflow-level agent override
        AgentDefinition? workflowAgent = null;
        if (context.WorkflowDefinition is not null)
        {
            workflowAgent = context.WorkflowDefinition.Agents
                .FirstOrDefault(a => a.Id.Equals(agentId, StringComparison.OrdinalIgnoreCase));
        }

        // Layer 3: Runtime overrides (passed via state context)
        AgentDefinition? runtimeOverride = null;
        if (context.State.Context.TryGetValue("__agentOverrides", out var overridesObj)
            && overridesObj is Dictionary<string, AgentDefinition> overrides
            && overrides.TryGetValue(agentId, out var rtOverride))
        {
            runtimeOverride = rtOverride;
        }

        if (globalAgent is null && workflowAgent is null && runtimeOverride is null)
            return null;

        // If only one layer exists, return it directly
        if (globalAgent is not null && workflowAgent is null && runtimeOverride is null)
            return globalAgent;
        if (globalAgent is null && workflowAgent is not null && runtimeOverride is null)
            return workflowAgent;
        if (globalAgent is null && workflowAgent is null && runtimeOverride is not null)
            return runtimeOverride;

        return AgentDefinitionMerger.Resolve(globalAgent, workflowAgent, runtimeOverride);
    }

    /// <summary>
    /// Resolves the system prompt with priority:
    /// input override → agent promptRef (resolved) → agent inline prompt → promptId input.
    /// </summary>
    internal static string? ResolveSystemPrompt(
        StepContext context,
        AgentDefinition? agent,
        IPromptRegistry? promptRegistry,
        PromptRenderer promptRenderer)
    {
        var inputPrompt = GetStringInput(context, "systemPrompt");
        if (!string.IsNullOrEmpty(inputPrompt))
            return RenderTemplate(inputPrompt, context, promptRenderer);

        if (agent is not null && !string.IsNullOrEmpty(agent.SystemPromptRef) && promptRegistry is not null)
        {
            var template = promptRegistry.GetPrompt(agent.SystemPromptRef);
            if (template is not null)
                return RenderTemplate(template.Content, context, promptRenderer);
        }

        if (agent is not null && !string.IsNullOrEmpty(agent.SystemPrompt))
            return RenderTemplate(agent.SystemPrompt, context, promptRenderer);

        var promptId = GetStringInput(context, "promptId");
        if (!string.IsNullOrEmpty(promptId) && promptRegistry is not null)
        {
            var template = promptRegistry.GetPrompt(promptId);
            if (template is not null)
                return RenderTemplate(template.Content, context, promptRenderer);
        }

        return null;
    }

    /// <summary>
    /// Renders a template string against the current workflow state and step inputs.
    /// First resolves namespaced paths (e.g. <c>{{inputs.request}}</c>, <c>{{nodes.X.Y}}</c>)
    /// via <see cref="StateMapper.RenderString"/>, then resolves any remaining bare-key
    /// placeholders (e.g. <c>{{fileName}}</c>) via <see cref="PromptRenderer"/>.
    /// </summary>
    internal static string RenderTemplate(string template, StepContext context, PromptRenderer promptRenderer)
    {
        // First pass: resolve namespaced state paths (inputs.X, context.X, nodes.X.Y, etc.)
        var resolved = StateMapper.RenderString(template, context.State);
        var intermediate = resolved?.ToString() ?? template;

        // Second pass: resolve bare variable names from the flat dictionary
        // (e.g. {{fileName}} from Context["fileName"] or step inputs)
        var variables = new Dictionary<string, object?>();

        foreach (var (key, value) in context.State.Inputs)
            variables[key] = value;

        foreach (var (key, value) in context.State.Context)
            variables[key] = value;

        foreach (var (key, value) in context.Inputs)
        {
            if (!key.StartsWith("__"))
                variables[key] = value;
        }

        return promptRenderer.Render(intermediate, variables);
    }

    internal static string? GetStringInput(StepContext context, string key)
        => context.Inputs.TryGetValue(key, out var val) ? val as string : null;

    internal static double GetDoubleInput(StepContext context, string key, double fallback)
    {
        if (!context.Inputs.TryGetValue(key, out var val) || val is null)
            return fallback;

        return val switch
        {
            double d => d,
            int i => i,
            float f => f,
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => fallback
        };
    }

    internal static int GetIntInput(StepContext context, string key, int fallback)
    {
        if (!context.Inputs.TryGetValue(key, out var val) || val is null)
            return fallback;

        return val switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => fallback
        };
    }
}