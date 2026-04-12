namespace Spectra.Contracts.Workflow;

/// <summary>
/// Loads workflow definitions by name from a backing store (file system, database, etc.).
/// </summary>
public interface IWorkflowStore
{
    WorkflowDefinition? Get(string name);
    IReadOnlyList<WorkflowDefinition> List();
}