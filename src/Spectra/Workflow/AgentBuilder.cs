using Spectra.Contracts.Workflow;

namespace Spectra.Workflow;

/// <summary>
/// Fluent builder for configuring an <see cref="AgentDefinition"/>.
/// </summary>
public class AgentBuilder
{
    private readonly string _id;
    private readonly string _provider;
    private readonly string _model;
    private double _temperature = 0.7;
    private int _maxTokens = 2048;
    private string? _systemPrompt;
    private string? _systemPromptRef;
    private string? _apiKeyEnvVar;
    private string? _apiKeyRef;
    private string? _apiVersionOverride;
    private string? _baseUrlOverride;
    private readonly List<string> _alternativeModels = [];

    // ── Multi-agent configuration ──
    private readonly List<string> _handoffTargets = [];
    private HandoffPolicy _handoffPolicy = HandoffPolicy.Allowed;
    private readonly List<string> _supervisorWorkers = [];
    private DelegationPolicy _delegationPolicy = DelegationPolicy.Allowed;
    private int _maxDelegationDepth = 3;
    private int _maxHandoffChainDepth = 5;
    private ConversationScope _conversationScope = ConversationScope.Handoff;
    private int _maxContextMessages = 10;
    private CyclePolicy _cyclePolicy = CyclePolicy.Deny;
    private string? _escalationTarget;
    private TimeSpan? _timeout;
    private readonly List<string> _stateReadPaths = [];
    private readonly List<string> _stateWritePaths = [];

    public AgentBuilder(string id, string provider, string model)
    {
        _id = id;
        _provider = provider;
        _model = model;
    }

    public AgentBuilder WithTemperature(double temperature)
    {
        _temperature = temperature;
        return this;
    }

    public AgentBuilder WithMaxTokens(int maxTokens)
    {
        _maxTokens = maxTokens;
        return this;
    }

    public AgentBuilder WithSystemPrompt(string prompt)
    {
        _systemPrompt = prompt;
        return this;
    }

    public AgentBuilder WithSystemPromptRef(string promptRef)
    {
        _systemPromptRef = promptRef;
        return this;
    }

    public AgentBuilder WithApiKeyEnvVar(string envVar)
    {
        _apiKeyEnvVar = envVar;
        return this;
    }

    public AgentBuilder WithApiKeyRef(string keyRef)
    {
        _apiKeyRef = keyRef;
        return this;
    }

    public AgentBuilder WithApiVersionOverride(string version)
    {
        _apiVersionOverride = version;
        return this;
    }

    public AgentBuilder WithBaseUrlOverride(string baseUrl)
    {
        _baseUrlOverride = baseUrl;
        return this;
    }

    public AgentBuilder WithAlternativeModel(string model)
    {
        _alternativeModels.Add(model);
        return this;
    }

    public AgentBuilder WithHandoffTargets(params string[] targets)
    {
        _handoffTargets.AddRange(targets);
        return this;
    }

    public AgentBuilder WithHandoffPolicy(HandoffPolicy policy)
    {
        _handoffPolicy = policy;
        return this;
    }

    public AgentBuilder AsSupervisor(params string[] workerAgentIds)
    {
        _supervisorWorkers.AddRange(workerAgentIds);
        return this;
    }

    public AgentBuilder WithDelegationPolicy(DelegationPolicy policy)
    {
        _delegationPolicy = policy;
        return this;
    }

    public AgentBuilder WithMaxDelegationDepth(int depth)
    {
        _maxDelegationDepth = depth;
        return this;
    }

    public AgentBuilder WithMaxHandoffChainDepth(int depth)
    {
        _maxHandoffChainDepth = depth;
        return this;
    }

    public AgentBuilder WithConversationScope(ConversationScope scope, int maxMessages = 10)
    {
        _conversationScope = scope;
        _maxContextMessages = maxMessages;
        return this;
    }

    public AgentBuilder WithCyclePolicy(CyclePolicy policy)
    {
        _cyclePolicy = policy;
        return this;
    }

    public AgentBuilder WithEscalationTarget(string agentId)
    {
        _escalationTarget = agentId;
        return this;
    }

    public AgentBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    public AgentBuilder WithStateReadPaths(params string[] paths)
    {
        _stateReadPaths.AddRange(paths);
        return this;
    }

    public AgentBuilder WithStateWritePaths(params string[] paths)
    {
        _stateWritePaths.AddRange(paths);
        return this;
    }

    public AgentDefinition Build() => new()
    {
        Id = _id,
        Provider = _provider,
        Model = _model,
        Temperature = _temperature,
        MaxTokens = _maxTokens,
        SystemPrompt = _systemPrompt,
        SystemPromptRef = _systemPromptRef,
        ApiKeyEnvVar = _apiKeyEnvVar,
        ApiKeyRef = _apiKeyRef,
        ApiVersionOverride = _apiVersionOverride,
        BaseUrlOverride = _baseUrlOverride,
        AlternativeModels = [.. _alternativeModels],
        // Multi-agent
        HandoffTargets = [.. _handoffTargets],
        HandoffPolicy = _handoffPolicy,
        SupervisorWorkers = [.. _supervisorWorkers],
        DelegationPolicy = _delegationPolicy,
        MaxDelegationDepth = _maxDelegationDepth,
        MaxHandoffChainDepth = _maxHandoffChainDepth,
        ConversationScope = _conversationScope,
        MaxContextMessages = _maxContextMessages,
        CyclePolicy = _cyclePolicy,
        EscalationTarget = _escalationTarget,
        Timeout = _timeout,
        StateReadPaths = [.. _stateReadPaths],
        StateWritePaths = [.. _stateWritePaths]
    };
}