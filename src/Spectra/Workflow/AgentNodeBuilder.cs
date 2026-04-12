using Spectra.Contracts.Workflow;

namespace Spectra.Workflow;

/// <summary>
/// Fluent builder for configuring an agent node.
/// Extends <see cref="NodeBuilder"/> with tool selection, iteration limits,
/// prompt configuration, and structured output options.
/// </summary>
public class AgentNodeBuilder : NodeBuilder
{
    private readonly List<string> _tools = [];
    private string? _userPrompt;
    private string? _userPromptRef;
    private int _maxIterations = 10;
    private int _tokenBudget;
    private string? _outputSchema;

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

    internal AgentNodeBuilder(string id, string agentId)
        : base(id, "agent")
    {
        WithAgent(agentId);
    }

    /// <summary>
    /// Specifies the tools available to the agent. Tool names must match
    /// registered tools in the <see cref="Spectra.Contracts.Tools.IToolRegistry"/>.
    /// Tools are required — an empty list means no tools and the agent
    /// behaves like a single-shot prompt.
    /// </summary>
    public AgentNodeBuilder WithTools(params string[] toolNames)
    {
        _tools.AddRange(toolNames);
        return this;
    }

    /// <summary>
    /// Sets the user prompt (the initial task/instruction for the agent).
    /// Supports template expressions like <c>{{inputs.task}}</c>.
    /// </summary>
    public AgentNodeBuilder WithUserPrompt(string prompt)
    {
        _userPrompt = prompt;
        return this;
    }

    /// <summary>
    /// Sets the user prompt via a reference to a prompt file in the registry.
    /// </summary>
    public AgentNodeBuilder WithUserPromptRef(string promptRef)
    {
        _userPromptRef = promptRef;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of LLM call iterations before the loop
    /// is forcefully stopped. Default is 10.
    /// </summary>
    public AgentNodeBuilder WithMaxIterations(int max)
    {
        _maxIterations = max;
        return this;
    }

    /// <summary>
    /// Sets a total token budget (input + output) for the agentic loop.
    /// When exceeded, the loop stops and returns a partial result.
    /// NOTE: Budget gating is tracked but not yet enforced — see cost-tracking ticket.
    /// </summary>
    public AgentNodeBuilder WithTokenBudget(int budget)
    {
        _tokenBudget = budget;
        return this;
    }

    /// <summary>
    /// When set, the agent's final response (the one without tool calls)
    /// is expected to be valid JSON conforming to this schema.
    /// The schema is validated and the parsed result is included in outputs.
    /// </summary>
    public AgentNodeBuilder WithOutputSchema(string jsonSchema)
    {
        _outputSchema = jsonSchema;
        return this;
    }

    // ── Multi-agent fluent methods ──

    /// <summary>
    /// Declares the agent IDs this agent can hand off to. Auto-injects the
    /// <c>transfer_to_agent</c> tool into the agent's tool set.
    /// </summary>
    public AgentNodeBuilder WithHandoffTargets(params string[] targets)
    {
        _handoffTargets.AddRange(targets);
        return this;
    }

    /// <summary>
    /// Sets the handoff policy: Allowed (default), RequiresApproval, or Disabled.
    /// </summary>
    public AgentNodeBuilder WithHandoffPolicy(HandoffPolicy policy)
    {
        _handoffPolicy = policy;
        return this;
    }

    /// <summary>
    /// Declares this agent as a supervisor with the given worker agent IDs.
    /// Auto-injects the <c>delegate_to_agent</c> tool.
    /// </summary>
    public AgentNodeBuilder AsSupervisor(params string[] workerAgentIds)
    {
        _supervisorWorkers.AddRange(workerAgentIds);
        return this;
    }

    /// <summary>
    /// Sets the delegation policy for this supervisor.
    /// </summary>
    public AgentNodeBuilder WithDelegationPolicy(DelegationPolicy policy)
    {
        _delegationPolicy = policy;
        return this;
    }

    /// <summary>
    /// Sets the max depth for nested delegations (supervisor → worker → sub-worker).
    /// </summary>
    public AgentNodeBuilder WithMaxDelegationDepth(int depth)
    {
        _maxDelegationDepth = depth;
        return this;
    }

    /// <summary>
    /// Sets the maximum handoff chain depth for this agent.
    /// </summary>
    public AgentNodeBuilder WithMaxHandoffChainDepth(int depth)
    {
        _maxHandoffChainDepth = depth;
        return this;
    }

    /// <summary>
    /// Configures how much conversation context is passed during handoffs.
    /// </summary>
    public AgentNodeBuilder WithConversationScope(ConversationScope scope, int maxMessages = 10)
    {
        _conversationScope = scope;
        _maxContextMessages = maxMessages;
        return this;
    }

    /// <summary>
    /// Sets the cycle policy for agent revisits in handoff chains.
    /// </summary>
    public AgentNodeBuilder WithCyclePolicy(CyclePolicy policy)
    {
        _cyclePolicy = policy;
        return this;
    }

    /// <summary>
    /// Sets an escalation target when this agent fails or exhausts its budget.
    /// Use "human" to trigger an interrupt for human escalation.
    /// </summary>
    public AgentNodeBuilder WithEscalationTarget(string agentId)
    {
        _escalationTarget = agentId;
        return this;
    }

    /// <summary>
    /// Sets a per-agent wall-clock timeout.
    /// </summary>
    public AgentNodeBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    internal new NodeDefinition Build()
    {
        if (_tools.Count > 0)
            WithParameter("tools", _tools.ToArray());

        if (_userPrompt is not null)
            WithParameter("userPrompt", _userPrompt);

        if (_userPromptRef is not null)
            WithParameter("userPromptRef", _userPromptRef);

        WithParameter("maxIterations", _maxIterations);

        if (_tokenBudget > 0)
            WithParameter("tokenBudget", _tokenBudget);

        if (_outputSchema is not null)
            WithParameter("outputSchema", _outputSchema);

        // Multi-agent parameters are stored on the AgentDefinition, not as node parameters.
        // The AgentNodeBuilder configures the agent via the WorkflowBuilder.AddAgent flow.
        // However, we store handoff/supervisor metadata as node parameters so the runner
        // and AgentStep can access them without looking up the agent definition separately.
        if (_handoffTargets.Count > 0)
            WithParameter("__handoffTargets", _handoffTargets.ToArray());

        if (_supervisorWorkers.Count > 0)
            WithParameter("__supervisorWorkers", _supervisorWorkers.ToArray());

        if (_escalationTarget is not null)
            WithParameter("__escalationTarget", _escalationTarget);

        if (_timeout is not null)
            WithParameter("__timeout", _timeout.Value.TotalSeconds);

        return base.Build();
    }
}