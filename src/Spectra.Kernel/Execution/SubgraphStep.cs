using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Built-in step that executes a child workflow (subgraph) with isolated state.
/// Parent/child data flow is controlled by the <see cref="SubgraphDefinition"/>
/// input/output mappings.
/// </summary>
public class SubgraphStep : IStep
{
    private readonly IWorkflowRunner _runner;

    public string StepType => "subgraph";

    public SubgraphStep(IWorkflowRunner runner)
    {
        _runner = runner;
    }

    public async Task<StepResult> ExecuteAsync(StepContext context)
    {
        // Locate the subgraph definition from the parent workflow
        var subgraphId = context.Inputs.TryGetValue("__subgraphId", out var sid)
            ? sid as string
            : null;

        if (string.IsNullOrEmpty(subgraphId))
            return StepResult.Fail("Subgraph node is missing '__subgraphId' in inputs.");

        var parentWorkflow = context.WorkflowDefinition;
        if (parentWorkflow == null)
            return StepResult.Fail("WorkflowDefinition not available in StepContext.");

        var subgraph = parentWorkflow.Subgraphs.FirstOrDefault(s => s.Id == subgraphId);
        if (subgraph == null)
            return StepResult.Fail($"Subgraph '{subgraphId}' not found in workflow '{parentWorkflow.Id}'.");

        // 1. Create isolated child state
        var childState = new WorkflowState
        {
            WorkflowId = subgraph.Workflow.Id,
            CorrelationId = context.RunId,
            RunId = $"{context.RunId}::{subgraphId}"
        };

        // 2. Apply input transform: parent state → child inputs
        foreach (var (parentPath, childKey) in subgraph.InputMappings)
        {
            var value = StateMapper.GetValueFromPath(context.State, parentPath);
            childState.Inputs[childKey] = value;
        }

        // Also forward any explicit node inputs (excluding internal keys)
        foreach (var (key, value) in context.Inputs)
        {
            if (!key.StartsWith("__") && !childState.Inputs.ContainsKey(key))
                childState.Inputs[key] = value;
        }

        // 3. Inherit parent agents that the child workflow doesn't already define.
        //    This allows child workflows to reference agents declared at the parent level
        //    without duplicating definitions, while still letting the child override if needed.
        var childAgentIds = new HashSet<string>(
            subgraph.Workflow.Agents.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var parentAgent in parentWorkflow.Agents)
        {
            if (!childAgentIds.Contains(parentAgent.Id))
                subgraph.Workflow.Agents.Add(parentAgent);
        }

        // 4. Execute child workflow with isolated state
        WorkflowState childResult;
        try
        {
            childResult = await _runner.RunAsync(
                subgraph.Workflow, childState, context.RunContext, context.CancellationToken);
        }
        catch (Exception ex)
        {
            return StepResult.Fail($"Subgraph '{subgraphId}' execution failed: {ex.Message}", ex);
        }

        // 5. Check for child errors
        if (childResult.Errors.Count > 0)
        {
            return StepResult.Fail(
                $"Subgraph '{subgraphId}' completed with errors: {string.Join("; ", childResult.Errors)}");
        }

        // 6. Apply output transform: child state → step outputs for parent
        var outputs = new Dictionary<string, object?>();

        foreach (var (childPath, parentPath) in subgraph.OutputMappings)
        {
            var value = StateMapper.GetValueFromPath(childResult, childPath);
            outputs[parentPath] = value;
        }

        // If no output mappings, expose the full child state sections as output
        if (subgraph.OutputMappings.Count == 0)
        {
            outputs["childContext"] = childResult.Context;
            outputs["childArtifacts"] = childResult.Artifacts;
        }

        return StepResult.Success(outputs);
    }
}