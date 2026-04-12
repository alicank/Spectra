using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;

namespace Spectra.Workflow;

/// <summary>
/// Fluent builder for configuring a session (conversational) node.
/// A session node is a long-lived agent that processes multiple user turns,
/// maintaining conversation history across checkpoint/resume cycles.
/// </summary>
public class SessionNodeBuilder : NodeBuilder
{
    private readonly List<string> _tools = [];
    private string? _systemPromptRef;
    private string? _systemPrompt;
    private SessionExitPolicy _exitPolicies = SessionExitPolicy.MaxTurns | SessionExitPolicy.LlmDecides;
    private int _maxTurns = 50;
    private int _tokenBudget;
    private TimeSpan? _timeout;
    private string? _exitCondition;
    private HistoryStrategy _historyStrategy = HistoryStrategy.Full;
    private int _maxHistoryMessages = 100;
    private string? _greetingPrompt;
    private readonly List<string> _exitCommands = ["/done", "/exit", "/quit"];

    internal SessionNodeBuilder(string id, string agentId)
        : base(id, "session")
    {
        WithAgent(agentId);
    }

    /// <summary>
    /// Specifies the tools available during the session. Tool names must match
    /// registered tools in the <see cref="Spectra.Contracts.Tools.IToolRegistry"/>.
    /// </summary>
    public SessionNodeBuilder WithTools(params string[] toolNames)
    {
        _tools.AddRange(toolNames);
        return this;
    }

    /// <summary>
    /// Sets the system prompt inline for the session agent.
    /// </summary>
    public SessionNodeBuilder WithSystemPrompt(string prompt)
    {
        _systemPrompt = prompt;
        return this;
    }

    /// <summary>
    /// Sets the system prompt via a reference to a prompt file in the registry.
    /// </summary>
    public SessionNodeBuilder WithSystemPromptRef(string promptRef)
    {
        _systemPromptRef = promptRef;
        return this;
    }

    /// <summary>
    /// Sets the exit policies that determine when the session completes.
    /// Multiple policies can be combined with bitwise OR.
    /// Default is <c>MaxTurns | LlmDecides</c>.
    /// </summary>
    public SessionNodeBuilder WithExitPolicy(SessionExitPolicy policies)
    {
        _exitPolicies = policies;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of conversation turns. Default is 50.
    /// Only effective when <see cref="SessionExitPolicy.MaxTurns"/> is active.
    /// </summary>
    public SessionNodeBuilder WithMaxTurns(int max)
    {
        _maxTurns = max;
        return this;
    }

    /// <summary>
    /// Sets a cumulative token budget (input + output) for the entire session.
    /// Only effective when <see cref="SessionExitPolicy.TokenBudget"/> is active.
    /// </summary>
    public SessionNodeBuilder WithTokenBudget(int budget)
    {
        _tokenBudget = budget;
        return this;
    }

    /// <summary>
    /// Sets a wall-clock timeout for the session.
    /// Only effective when <see cref="SessionExitPolicy.Timeout"/> is active.
    /// </summary>
    public SessionNodeBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets a state condition expression that, when true, exits the session.
    /// Only effective when <see cref="SessionExitPolicy.Condition"/> is active.
    /// </summary>
    public SessionNodeBuilder WithExitCondition(string expression)
    {
        _exitCondition = expression;
        return this;
    }

    /// <summary>
    /// Controls how conversation history is managed.
    /// Default is <see cref="HistoryStrategy.Full"/>.
    /// </summary>
    public SessionNodeBuilder WithHistoryStrategy(HistoryStrategy strategy, int maxMessages = 100)
    {
        _historyStrategy = strategy;
        _maxHistoryMessages = maxMessages;
        return this;
    }

    /// <summary>
    /// Sets an optional greeting prompt. When set, the session generates an
    /// initial assistant message on first entry (before any user message).
    /// Supports template expressions.
    /// </summary>
    public SessionNodeBuilder WithGreeting(string prompt)
    {
        _greetingPrompt = prompt;
        return this;
    }

    /// <summary>
    /// Configures the user commands that trigger session exit.
    /// Only effective when <see cref="SessionExitPolicy.UserCommand"/> is active.
    /// Default is "/done", "/exit", "/quit".
    /// </summary>
    public SessionNodeBuilder WithExitCommands(params string[] commands)
    {
        _exitCommands.Clear();
        _exitCommands.AddRange(commands);
        return this;
    }

    internal new NodeDefinition Build()
    {
        if (_tools.Count > 0)
            WithParameter("tools", _tools.ToArray());

        if (_systemPrompt is not null)
            WithParameter("systemPrompt", _systemPrompt);

        if (_systemPromptRef is not null)
            WithParameter("systemPromptRef", _systemPromptRef);

        WithParameter("__exitPolicies", (int)_exitPolicies);
        WithParameter("__maxTurns", _maxTurns);
        WithParameter("__historyStrategy", _historyStrategy.ToString());
        WithParameter("__maxHistoryMessages", _maxHistoryMessages);

        if (_tokenBudget > 0)
            WithParameter("__tokenBudget", _tokenBudget);

        if (_timeout is not null)
            WithParameter("__timeout", _timeout.Value.TotalSeconds);

        if (_exitCondition is not null)
            WithParameter("__exitCondition", _exitCondition);

        if (_greetingPrompt is not null)
            WithParameter("__greetingPrompt", _greetingPrompt);

        if (_exitCommands.Count > 0)
            WithParameter("__exitCommands", _exitCommands.ToArray());

        return base.Build();
    }
}