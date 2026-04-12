using Spectra.Contracts.Workflow;

namespace Spectra.Workflow;

/// <summary>
/// Fluent builder for configuring a single <see cref="NodeDefinition"/>.
/// </summary>
public class NodeBuilder
{
    private readonly string _id;
    private readonly string _stepType;
    private string? _agentId;
    private bool _waitForAll;
    private string? _interruptBefore;
    private string? _interruptAfter;
    private readonly Dictionary<string, object?> _parameters = [];
    private readonly Dictionary<string, string> _inputMappings = [];
    private readonly Dictionary<string, string> _outputMappings = [];
    private string? _subgraphId;

    internal NodeBuilder(string id, string stepType)
    {
        _id = id;
        _stepType = stepType;
    }

    /// <summary>Associates this node with a registered agent.</summary>
    public NodeBuilder WithAgent(string agentId)
    {
        _agentId = agentId;
        return this;
    }

    /// <summary>Configures this node to wait for all incoming edges before executing.</summary>
    public NodeBuilder WaitForAll(bool wait = true)
    {
        _waitForAll = wait;
        return this;
    }

    /// <summary>Sets a parameter on this node.</summary>
    public NodeBuilder WithParameter(string key, object? value)
    {
        _parameters[key] = value;
        return this;
    }

    /// <summary>Maps a workflow state field to a node input.</summary>
    public NodeBuilder MapInput(string inputKey, string statePath)
    {
        _inputMappings[inputKey] = statePath;
        return this;
    }

    /// <summary>Maps a node output to a workflow state field.</summary>
    public NodeBuilder MapOutput(string outputKey, string statePath)
    {
        _outputMappings[outputKey] = statePath;
        return this;
    }

    /// <summary>Configures the runner to pause BEFORE executing this node.</summary>
    public NodeBuilder InterruptBefore(string reason = "Interrupt before node execution")
    {
        _interruptBefore = reason;
        return this;
    }

    /// <summary>Configures the runner to pause AFTER executing this node.</summary>
    public NodeBuilder InterruptAfter(string reason = "Interrupt after node execution")
    {
        _interruptAfter = reason;
        return this;
    }

    /// <summary>
    /// Links this node to a subgraph, causing it to execute a child workflow
    /// with isolated state. Automatically sets StepType to "subgraph".
    /// </summary>
    public NodeBuilder AsSubgraph(string subgraphId)
    {
        _subgraphId = subgraphId;
        return this;
    }


    internal NodeDefinition Build()
    {
        var stepType = _subgraphId != null ? "subgraph" : _stepType;

        return new NodeDefinition
        {
            Id = _id,
            StepType = stepType,
            AgentId = _agentId,
            SubgraphId = _subgraphId,
            WaitForAll = _waitForAll,
            Parameters = new(_parameters),
            InputMappings = new(_inputMappings),
            OutputMappings = new(_outputMappings),
            InterruptBefore = _interruptBefore,
            InterruptAfter = _interruptAfter
        };
    }
}