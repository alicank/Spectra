using Spectra.Contracts.Workflow;

namespace Spectra.Workflow;

/// <summary>
/// Fluent builder for configuring a <see cref="SubgraphDefinition"/>.
/// </summary>
public class SubgraphBuilder
{
    private readonly string _id;
    private readonly WorkflowDefinition _workflow;
    private readonly Dictionary<string, string> _inputMappings = [];
    private readonly Dictionary<string, string> _outputMappings = [];

    internal SubgraphBuilder(string id, WorkflowDefinition workflow)
    {
        _id = id;
        _workflow = workflow;
    }

    /// <summary>
    /// Maps a parent state path to a child workflow input key.
    /// Example: MapInput("Context.documents", "items") copies parent's Context.documents
    /// into the child's Inputs["items"].
    /// </summary>
    public SubgraphBuilder MapInput(string parentStatePath, string childInputKey)
    {
        _inputMappings[parentStatePath] = childInputKey;
        return this;
    }

    /// <summary>
    /// Maps a child state path to a parent state path.
    /// Example: MapOutput("Artifacts.result", "Context.subResult") copies the child's
    /// Artifacts.result back into the parent's Context.subResult after completion.
    /// </summary>
    public SubgraphBuilder MapOutput(string childStatePath, string parentStatePath)
    {
        _outputMappings[childStatePath] = parentStatePath;
        return this;
    }

    internal SubgraphDefinition Build() => new()
    {
        Id = _id,
        Workflow = _workflow,
        InputMappings = new(_inputMappings),
        OutputMappings = new(_outputMappings)
    };
}