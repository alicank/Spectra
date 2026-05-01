using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.State;
using Xunit;

namespace Spectra.Tests.Checkpointing;

/// <summary>
/// Contract compliance test suite for <see cref="ICheckpointStore"/> implementations.
/// Inherit this class, implement <see cref="CreateStore"/>, and all contract tests will run
/// automatically against your store.
/// </summary>
/// <typeparam name="T">The concrete <see cref="ICheckpointStore"/> implementation.</typeparam>
public abstract class CheckpointStoreTestBase<T> where T : ICheckpointStore
{
    protected abstract T CreateStore();

    private Checkpoint CreateCheckpoint(
        string? runId = null,
        string? workflowId = null,
        CheckpointStatus status = CheckpointStatus.InProgress)
    {
        return new Checkpoint
        {
            RunId = runId ?? Guid.NewGuid().ToString(),
            WorkflowId = workflowId ?? "test-workflow",
            State = new WorkflowState(),
            LastCompletedNodeId = "node-1",
            NextNodeId = "node-2",
            StepsCompleted = 1,
            Status = status
        };
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var store = CreateStore();
        var checkpoint = CreateCheckpoint();

        await store.SaveAsync(checkpoint);
        var loaded = await store.LoadAsync(checkpoint.RunId);

        Assert.NotNull(loaded);
        Assert.Equal(checkpoint.RunId, loaded!.RunId);
        Assert.Equal(checkpoint.WorkflowId, loaded.WorkflowId);
        Assert.Equal(checkpoint.LastCompletedNodeId, loaded.LastCompletedNodeId);
        Assert.Equal(checkpoint.NextNodeId, loaded.NextNodeId);
        Assert.Equal(checkpoint.StepsCompleted, loaded.StepsCompleted);
        Assert.Equal(checkpoint.Status, loaded.Status);
    }

    [Fact]
    public async Task Load_NonExistentRunId_ReturnsNull()
    {
        var store = CreateStore();

        var result = await store.LoadAsync("does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public async Task Save_OverwritesExisting()
    {
        var store = CreateStore();
        var original = CreateCheckpoint();
        await store.SaveAsync(original);

        var updated = original with
        {
            StepsCompleted = 5,
            Status = CheckpointStatus.Completed
        };
        await store.SaveAsync(updated);

        var loaded = await store.LoadAsync(original.RunId);

        Assert.NotNull(loaded);
        Assert.Equal(5, loaded!.StepsCompleted);
        Assert.Equal(CheckpointStatus.Completed, loaded.Status);
    }

    [Fact]
    public async Task SaveAndLoad_PreservesCancelledStatus()
    {
        // CheckpointStatus.Cancelled is distinct from Failed — stores must round-trip it faithfully
        // so that consumers can tell an operator-cancelled run from a failed one.
        var store = CreateStore();
        var checkpoint = CreateCheckpoint(status: CheckpointStatus.Cancelled);

        await store.SaveAsync(checkpoint);
        var loaded = await store.LoadAsync(checkpoint.RunId);

        Assert.NotNull(loaded);
        Assert.Equal(CheckpointStatus.Cancelled, loaded!.Status);
    }

    [Fact]
    public async Task Delete_RemovesCheckpoint()
    {
        var store = CreateStore();
        var checkpoint = CreateCheckpoint();
        await store.SaveAsync(checkpoint);

        await store.DeleteAsync(checkpoint.RunId);

        var loaded = await store.LoadAsync(checkpoint.RunId);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task Delete_NonExistent_DoesNotThrow()
    {
        var store = CreateStore();

        var exception = await Record.ExceptionAsync(
            () => store.DeleteAsync("does-not-exist"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ListAsync_ReturnsAll_WhenNoFilter()
    {
        var store = CreateStore();
        var cp1 = CreateCheckpoint(workflowId: "wf-a");
        var cp2 = CreateCheckpoint(workflowId: "wf-b");
        await store.SaveAsync(cp1);
        await store.SaveAsync(cp2);

        var results = await store.ListAsync();

        Assert.True(results.Count >= 2);
    }

    [Fact]
    public async Task ListAsync_FiltersByWorkflowId()
    {
        var store = CreateStore();
        var cp1 = CreateCheckpoint(workflowId: "wf-target");
        var cp2 = CreateCheckpoint(workflowId: "wf-other");
        await store.SaveAsync(cp1);
        await store.SaveAsync(cp2);

        var results = await store.ListAsync("wf-target");

        Assert.All(results, c => Assert.Equal("wf-target", c.WorkflowId));
    }

    [Fact]
    public async Task LoadLatestAsync_ReturnsNewest()
    {
        var store = CreateStore();
        var older = CreateCheckpoint(workflowId: "wf-latest") with
        {
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        var newer = CreateCheckpoint(workflowId: "wf-latest") with
        {
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await store.SaveAsync(older);
        await store.SaveAsync(newer);

        var latest = await store.LoadLatestAsync("wf-latest");

        Assert.NotNull(latest);
        Assert.Equal(newer.RunId, latest!.RunId);
    }

    [Fact]
    public async Task LoadLatestAsync_NoMatch_ReturnsNull()
    {
        var store = CreateStore();

        var result = await store.LoadLatestAsync("no-such-workflow");

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAndLoad_PreservesState()
    {
        var store = CreateStore();
        var state = new WorkflowState
        {
            WorkflowId = "wf-state",
            CurrentNodeId = "node-5"
        };
        state.Context["key"] = "value";

        var checkpoint = new Checkpoint
        {
            RunId = Guid.NewGuid().ToString(),
            WorkflowId = "wf-state",
            State = state,
            StepsCompleted = 3,
            Status = CheckpointStatus.InProgress
        };

        await store.SaveAsync(checkpoint);
        var loaded = await store.LoadAsync(checkpoint.RunId);

        Assert.NotNull(loaded);
        Assert.Equal("wf-state", loaded!.State.WorkflowId);
        Assert.Equal("node-5", loaded.State.CurrentNodeId);
    }

    [Fact]
    public async Task SaveMultiple_AssignsSequentialIndexes()
    {
        var store = CreateStore();
        var runId = Guid.NewGuid().ToString();

        var cp1 = CreateCheckpoint(runId: runId);
        var cp2 = CreateCheckpoint(runId: runId) with
        {
            StepsCompleted = 2,
            UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(1)
        };

        await store.SaveAsync(cp1);
        await store.SaveAsync(cp2);

        var history = await store.ListByRunAsync(runId);

        Assert.Equal(2, history.Count);
        Assert.Equal(0, history[0].Index);
        Assert.Equal(1, history[1].Index);
    }

    [Fact]
    public async Task LoadByIndex_ReturnsCorrectCheckpoint()
    {
        var store = CreateStore();
        var runId = Guid.NewGuid().ToString();

        var cp1 = CreateCheckpoint(runId: runId);
        var cp2 = CreateCheckpoint(runId: runId) with
        {
            StepsCompleted = 2,
            LastCompletedNodeId = "node-2",
            UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(1)
        };

        await store.SaveAsync(cp1);
        await store.SaveAsync(cp2);

        var loaded = await store.LoadByIndexAsync(runId, 0);

        Assert.NotNull(loaded);
        Assert.Equal(0, loaded!.Index);
        Assert.Equal("node-1", loaded.LastCompletedNodeId);
    }

    [Fact]
    public async Task LoadByIndex_InvalidIndex_ReturnsNull()
    {
        var store = CreateStore();
        var runId = Guid.NewGuid().ToString();
        await store.SaveAsync(CreateCheckpoint(runId: runId));

        var result = await store.LoadByIndexAsync(runId, 999);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListByRun_ReturnsCheckpointsForRun()
    {
        var store = CreateStore();
        var runId = Guid.NewGuid().ToString();
        var otherRunId = Guid.NewGuid().ToString();

        await store.SaveAsync(CreateCheckpoint(runId: runId));
        await store.SaveAsync(CreateCheckpoint(runId: runId));
        await store.SaveAsync(CreateCheckpoint(runId: otherRunId));

        var results = await store.ListByRunAsync(runId);

        Assert.Equal(2, results.Count);
        Assert.All(results, c => Assert.Equal(runId, c.RunId));
    }

    [Fact]
    public async Task Fork_CreatesNewRunFromCheckpoint()
    {
        var store = CreateStore();
        var sourceRunId = Guid.NewGuid().ToString();
        var newRunId = Guid.NewGuid().ToString();

        var cp = CreateCheckpoint(runId: sourceRunId, workflowId: "wf-fork");
        await store.SaveAsync(cp);

        var forked = await store.ForkAsync(sourceRunId, 0, newRunId);

        Assert.Equal(newRunId, forked.RunId);
        Assert.Equal("wf-fork", forked.WorkflowId);
        Assert.Equal(sourceRunId, forked.ParentRunId);
        Assert.Equal(0, forked.ParentCheckpointIndex);
        Assert.Equal(CheckpointStatus.InProgress, forked.Status);
    }

    [Fact]
    public async Task Fork_AppliesStateOverrides()
    {
        var store = CreateStore();
        var sourceRunId = Guid.NewGuid().ToString();
        var newRunId = Guid.NewGuid().ToString();

        var state = new WorkflowState { WorkflowId = "wf-override" };
        state.Context["original"] = "yes";

        var cp = new Checkpoint
        {
            RunId = sourceRunId,
            WorkflowId = "wf-override",
            State = state,
            StepsCompleted = 1,
            Status = CheckpointStatus.InProgress
        };
        await store.SaveAsync(cp);

        var overrides = new WorkflowState();
        overrides.Context["injected"] = "value";

        var forked = await store.ForkAsync(sourceRunId, 0, newRunId, overrides);

        Assert.Equal("value", forked.State.Context["injected"]?.ToString());
    }

    [Fact]
    public async Task Fork_InvalidIndex_Throws()
    {
        var store = CreateStore();
        var sourceRunId = Guid.NewGuid().ToString();
        await store.SaveAsync(CreateCheckpoint(runId: sourceRunId));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.ForkAsync(sourceRunId, 999, Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task GetLineage_ReturnsAncestorChain()
    {
        var store = CreateStore();
        var grandparentRunId = Guid.NewGuid().ToString();
        var parentRunId = Guid.NewGuid().ToString();
        var childRunId = Guid.NewGuid().ToString();

        await store.SaveAsync(CreateCheckpoint(runId: grandparentRunId, workflowId: "wf-lineage"));
        await store.ForkAsync(grandparentRunId, 0, parentRunId);
        await store.ForkAsync(parentRunId, 0, childRunId);

        var lineage = await store.GetLineageAsync(childRunId);

        Assert.Equal(3, lineage.Count);
        Assert.Equal(grandparentRunId, lineage[0].RunId);
        Assert.Equal(parentRunId, lineage[1].RunId);
        Assert.Equal(childRunId, lineage[2].RunId);
    }

    [Fact]
    public async Task Purge_RemovesAllCheckpointsForRun()
    {
        var store = CreateStore();
        var runId = Guid.NewGuid().ToString();
        await store.SaveAsync(CreateCheckpoint(runId: runId));
        await store.SaveAsync(CreateCheckpoint(runId: runId));

        await store.PurgeAsync(runId);

        var results = await store.ListByRunAsync(runId);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Purge_NonExistent_DoesNotThrow()
    {
        var store = CreateStore();

        var exception = await Record.ExceptionAsync(
            () => store.PurgeAsync("does-not-exist"));

        Assert.Null(exception);
    }
}