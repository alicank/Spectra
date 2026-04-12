namespace Spectra.Contracts.Workflow;

/// <summary>
/// Defines a subgraph: a child workflow that executes with isolated state.
/// Input/output mappings control how data flows between parent and child.
/// </summary>
public class SubgraphDefinition
{
    /// <summary>Unique identifier for this subgraph within the parent workflow.</summary>
    public required string Id { get; set; }

    /// <summary>The child workflow definition to execute.</summary>
    public required WorkflowDefinition Workflow { get; set; }

    /// <summary>
    /// Maps parent state paths to child workflow input keys.
    /// Key = parent state path (e.g. "Context.documents"), Value = child input key (e.g. "items").
    /// The resolved parent values are placed into the child's Inputs dictionary.
    /// </summary>
    public Dictionary<string, string> InputMappings { get; set; } = [];

    /// <summary>
    /// Maps child state paths to parent state paths.
    /// Key = child state path (e.g. "Artifacts.result"), Value = parent state path (e.g. "Context.subResult").
    /// After the child workflow completes, these values are copied back to the parent.
    /// </summary>
    public Dictionary<string, string> OutputMappings { get; set; } = [];
}