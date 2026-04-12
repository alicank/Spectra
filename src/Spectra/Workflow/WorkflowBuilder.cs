using Spectra.Contracts.State;
using Spectra.Contracts.Workflow;

namespace Spectra.Workflow;

/// <summary>
/// Fluent builder for constructing <see cref="WorkflowDefinition"/> graphs in code.
/// </summary>
public class WorkflowBuilder
{
    private readonly string _id;
    private string? _name;
    private string? _description;
    private int _version = 1;
    private string? _entryNodeId;
    private int _maxConcurrency = 4;
    private TimeSpan _defaultTimeout = TimeSpan.FromMinutes(5);
    private int _maxNodeIterations = 100;
    private int _globalTokenBudget;
    private int _maxHandoffChainDepth = 10;
    private int _maxTotalAgentIterations = 500;

    private readonly List<NodeDefinition> _nodes = [];
    private readonly List<EdgeDefinition> _edges = [];
    private readonly List<AgentDefinition> _agents = [];
    private readonly List<StateFieldDefinition> _stateFields = [];
    private readonly List<SubgraphDefinition> _subgraphs = [];

    private WorkflowBuilder(string id) => _id = id;

    /// <summary>Creates a new builder for a workflow with the given identifier.</summary>
    public static WorkflowBuilder Create(string id) => new(id);

    public WorkflowBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public WorkflowBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public WorkflowBuilder WithVersion(int version)
    {
        _version = version;
        return this;
    }

    public WorkflowBuilder WithMaxConcurrency(int max)
    {
        _maxConcurrency = max;
        return this;
    }

    public WorkflowBuilder WithDefaultTimeout(TimeSpan timeout)
    {
        _defaultTimeout = timeout;
        return this;
    }

    public WorkflowBuilder WithMaxNodeIterations(int max)
    {
        _maxNodeIterations = max;
        return this;
    }

    /// <summary>Sets the global token budget across all agents in a run.</summary>
    public WorkflowBuilder WithGlobalTokenBudget(int budget)
    {
        _globalTokenBudget = budget;
        return this;
    }

    /// <summary>Sets the maximum handoff chain depth for the entire workflow.</summary>
    public WorkflowBuilder WithMaxHandoffChainDepth(int depth)
    {
        _maxHandoffChainDepth = depth;
        return this;
    }

    /// <summary>Sets the maximum total LLM iterations across all agent nodes.</summary>
    public WorkflowBuilder WithMaxTotalAgentIterations(int max)
    {
        _maxTotalAgentIterations = max;
        return this;
    }

    /// <summary>Sets the entry point of the workflow.</summary>
    public WorkflowBuilder SetEntryNode(string nodeId)
    {
        _entryNodeId = nodeId;
        return this;
    }

    /// <summary>Adds a node with the given step type and optional configuration.</summary>
    public WorkflowBuilder AddNode(string id, string stepType, Action<NodeBuilder>? configure = null)
    {
        var builder = new NodeBuilder(id, stepType);
        configure?.Invoke(builder);
        _nodes.Add(builder.Build());
        return this;
    }

    /// <summary>Adds a directed edge between two nodes.</summary>
    public WorkflowBuilder AddEdge(string from, string to, string? condition = null, bool isLoopback = false)
    {
        _edges.Add(new EdgeDefinition
        {
            From = from,
            To = to,
            Condition = condition,
            IsLoopback = isLoopback
        });
        return this;
    }

    /// <summary>Registers an agent definition for use by agent-typed nodes.</summary>
    public WorkflowBuilder AddAgent(string id, string provider, string model, Action<AgentBuilder>? configure = null)
    {
        var builder = new AgentBuilder(id, provider, model);
        configure?.Invoke(builder);
        _agents.Add(builder.Build());
        return this;
    }

    /// <summary>Declares a state field in the workflow schema.</summary>
    public WorkflowBuilder AddStateField(string path, Type valueType, string? reducerKey = null)
    {
        _stateFields.Add(new StateFieldDefinition
        {
            Path = path,
            ValueType = valueType,
            ReducerKey = reducerKey
        });
        return this;
    }

    /// <summary>Registers a subgraph (child workflow) for use by subgraph-typed nodes.</summary>
    public WorkflowBuilder AddSubgraph(string id, WorkflowDefinition childWorkflow, Action<SubgraphBuilder>? configure = null)
    {
        var builder = new SubgraphBuilder(id, childWorkflow);
        configure?.Invoke(builder);
        _subgraphs.Add(builder.Build());
        return this;
    }

    /// <summary>Adds a node that executes a subgraph with isolated state.</summary>
    public WorkflowBuilder AddSubgraphNode(string nodeId, string subgraphId, Action<NodeBuilder>? configure = null)
    {
        var builder = new NodeBuilder(nodeId, "subgraph");
        builder.AsSubgraph(subgraphId);
        configure?.Invoke(builder);
        _nodes.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Adds an agent node — an autonomous tool-using agent that iterates
    /// until it produces a final response or reaches a guard limit.
    /// </summary>
    public WorkflowBuilder AddAgentNode(string id, string agentId, Action<AgentNodeBuilder>? configure = null)
    {
        var builder = new AgentNodeBuilder(id, agentId);
        configure?.Invoke(builder);
        _nodes.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Adds a session node — a long-lived conversational agent that processes
    /// multiple user turns, maintaining conversation history across suspend/resume cycles.
    /// The session suspends after each assistant response and resumes when a new
    /// user message is delivered via <c>SendMessageAsync</c>.
    /// </summary>
    public WorkflowBuilder AddSessionNode(string id, string agentId, Action<SessionNodeBuilder>? configure = null)
    {
        var builder = new SessionNodeBuilder(id, agentId);
        configure?.Invoke(builder);
        _nodes.Add(builder.Build());
        return this;
    }


    /// <summary>Declares a state field in the workflow schema using a generic type parameter.</summary>
    public WorkflowBuilder AddStateField<T>(string path, string? reducerKey = null)
        => AddStateField(path, typeof(T), reducerKey);

    /// <summary>Builds the final <see cref="WorkflowDefinition"/>.</summary>
    public WorkflowDefinition Build()
    {
        if (_nodes.Count == 0)
            throw new InvalidOperationException("Workflow must contain at least one node.");

        var entryNodeId = _entryNodeId ?? _nodes[0].Id;

        if (_nodes.All(n => n.Id != entryNodeId))
            throw new InvalidOperationException($"Entry node '{entryNodeId}' not found in defined nodes.");

        return new WorkflowDefinition
        {
            Id = _id,
            Name = _name,
            Description = _description,
            Version = _version,
            EntryNodeId = entryNodeId,
            Nodes = [.. _nodes],
            Edges = [.. _edges],
            Agents = [.. _agents],
            StateFields = [.. _stateFields],
            Subgraphs = [.. _subgraphs],
            MaxConcurrency = _maxConcurrency,
            DefaultTimeout = _defaultTimeout,
            MaxNodeIterations = _maxNodeIterations,
            GlobalTokenBudget = _globalTokenBudget,
            MaxHandoffChainDepth = _maxHandoffChainDepth,
            MaxTotalAgentIterations = _maxTotalAgentIterations
        };
    }
}