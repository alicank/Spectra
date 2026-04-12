using Spectra.Contracts.Workflow;

namespace Spectra.Contracts.State;

/// <summary>
/// Bridges workflow state and step inputs/outputs using the
/// InputMappings / OutputMappings declared on a <see cref="NodeDefinition"/>.
/// </summary>
public interface IStateMapper
{
    /// <summary>
    /// Resolves the effective input dictionary for a step by merging
    /// node parameters, input mappings, and template rendering.
    /// </summary>
    Dictionary<string, object?> ResolveInputs(NodeDefinition node, WorkflowState state);

    /// <summary>
    /// Applies step output values back into workflow state according
    /// to the node's output mappings.
    /// </summary>
    void ApplyOutputs(NodeDefinition node, WorkflowState state, Dictionary<string, object?> outputs);
}
