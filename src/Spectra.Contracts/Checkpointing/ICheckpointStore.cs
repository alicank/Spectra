using Spectra.Contracts.State;

namespace Spectra.Contracts.Checkpointing;

public interface ICheckpointStore
{
    Task SaveAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default);

    Task<Checkpoint?> LoadAsync(string runId, CancellationToken cancellationToken = default);

    Task<Checkpoint?> LoadLatestAsync(
        string workflowId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string runId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Checkpoint>> ListAsync(
        string? workflowId = null,
        CancellationToken cancellationToken = default);

    Task<Checkpoint?> LoadByIndexAsync(
        string runId,
        int index,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Checkpoint>> ListByRunAsync(
        string runId,
        CancellationToken cancellationToken = default);

    Task<Checkpoint> ForkAsync(
        string sourceRunId,
        int checkpointIndex,
        string newRunId,
        WorkflowState? stateOverrides = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Checkpoint>> GetLineageAsync(
        string runId,
        CancellationToken cancellationToken = default);

    Task PurgeAsync(
        string runId,
        CancellationToken cancellationToken = default);
}