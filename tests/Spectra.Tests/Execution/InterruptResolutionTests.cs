using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

/// <summary>
/// Covers the runner's interpretation of <see cref="InterruptResponse.Status"/> when
/// resuming a run via <see cref="IWorkflowRunner.ResumeWithResponseAsync"/>.
///
/// Semantics under test:
///   Approved   → guarded step executes; final <see cref="WorkflowRunStatus.Completed"/>
///   Rejected   → guarded step is skipped; final <see cref="WorkflowRunStatus.Failed"/> + error
///   TimedOut   → guarded step is skipped; final <see cref="WorkflowRunStatus.Failed"/> + error
///   Cancelled  → guarded step is skipped; final <see cref="WorkflowRunStatus.Cancelled"/>, NO error
///
/// Plus: a cancelled run cannot be resumed via <see cref="IWorkflowRunner.ResumeAsync"/>.
/// </summary>
public class InterruptResolutionTests
{
    // ─── Helpers (minimal, scoped to this file) ─────────────────────

    private static WorkflowRunner CreateRunner(
        IStepRegistry registry,
        ICheckpointStore checkpointStore) =>
        new(
            registry,
            new StateMapper(),
            checkpointStore: checkpointStore);

    private sealed class InMemoryStepRegistry : IStepRegistry
    {
        private readonly Dictionary<string, IStep> _steps = new(StringComparer.OrdinalIgnoreCase);
        public IStep? GetStep(string stepType) => _steps.GetValueOrDefault(stepType);
        public void Register(IStep step) => _steps[step.StepType] = step;
    }

    private sealed class LambdaStep : IStep
    {
        private readonly Func<StepContext, StepResult> _execute;
        public string StepType { get; }
        public LambdaStep(string stepType, Func<StepContext, StepResult> execute)
            => (StepType, _execute) = (stepType, execute);
        public Task<StepResult> ExecuteAsync(StepContext context)
            => Task.FromResult(_execute(context));
    }

    /// <summary>
    /// Minimal in-memory checkpoint store. Shared across the tests in this file.
    /// (We use a dedicated helper rather than the production <c>InMemoryCheckpointStore</c>
    /// to keep this file self-contained and match the pattern in RunflowRunnerTests.)
    /// </summary>
    private sealed class InMemoryCheckpointStore : ICheckpointStore
    {
        private readonly Dictionary<string, List<Checkpoint>> _store = [];

        public Task SaveAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default)
        {
            if (!_store.TryGetValue(checkpoint.RunId, out var list))
            {
                list = [];
                _store[checkpoint.RunId] = list;
            }
            list.Add(checkpoint with { Index = list.Count });
            return Task.CompletedTask;
        }

        public Task<Checkpoint?> LoadAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(runId, out var l) && l.Count > 0 ? l[^1] : null);

        public Task<Checkpoint?> LoadLatestAsync(string workflowId, CancellationToken ct = default) =>
            Task.FromResult(_store.Values.SelectMany(l => l)
                .Where(c => c.WorkflowId == workflowId)
                .OrderByDescending(c => c.UpdatedAt).FirstOrDefault());

        public Task DeleteAsync(string runId, CancellationToken ct = default)
        { _store.Remove(runId); return Task.CompletedTask; }

        public Task<IReadOnlyList<Checkpoint>> ListAsync(string? workflowId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Checkpoint>>(_store.Values.SelectMany(l => l)
                .Where(c => workflowId == null || c.WorkflowId == workflowId).ToList());

        public Task<Checkpoint?> LoadByIndexAsync(string runId, int index, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(runId, out var l) && index >= 0 && index < l.Count ? l[index] : null);

        public Task<IReadOnlyList<Checkpoint>> ListByRunAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Checkpoint>>(_store.TryGetValue(runId, out var l) ? l.ToList() : []);

        public Task<Checkpoint> ForkAsync(string src, int idx, string newId,
            WorkflowState? overrides = null, CancellationToken ct = default) =>
            throw new NotImplementedException("Not needed in these tests");

        public Task<IReadOnlyList<Checkpoint>> GetLineageAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Checkpoint>>([]);

        public Task PurgeAsync(string runId, CancellationToken ct = default)
        { _store.Remove(runId); return Task.CompletedTask; }
    }

    /// <summary>
    /// Builds a two-node workflow: <c>prepare</c> runs unconditionally, then <c>execute</c>
    /// is guarded by <c>interruptBefore</c>. This gives us the canonical shape for
    /// testing interrupt-resume semantics: if the interrupt says "proceed", execute runs
    /// and the run completes; if it says "stop", execute never runs.
    /// </summary>
    private static (WorkflowDefinition Workflow, Func<bool> ExecuteRan) BuildGuardedWorkflow(
        InMemoryStepRegistry registry)
    {
        var executeRan = false;

        registry.Register(new LambdaStep("prepare", _ =>
            StepResult.Success(new() { ["prepared"] = true })));

        registry.Register(new LambdaStep("execute", _ =>
        {
            executeRan = true;
            return StepResult.Success(new() { ["executed"] = true });
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "guarded-wf",
            EntryNodeId = "prepare",
            Nodes =
            [
                new NodeDefinition { Id = "prepare", StepType = "prepare" },
                new NodeDefinition
                {
                    Id = "execute",
                    StepType = "execute",
                    InterruptBefore = "Human approval required"
                }
            ],
            Edges = [new EdgeDefinition { From = "prepare", To = "execute" }]
        };

        return (workflow, () => executeRan);
    }

    /// <summary>
    /// Runs the workflow to the interrupt point, then resumes with the given response.
    /// Returns the final state plus a probe telling us whether <c>execute</c> ran.
    /// </summary>
    private static async Task<(WorkflowState State, bool ExecuteRan)> RunAndResumeAsync(
        InterruptResponse response)
    {
        var store = new InMemoryCheckpointStore();
        var registry = new InMemoryStepRegistry();
        var (workflow, executeRan) = BuildGuardedWorkflow(registry);
        var runner = CreateRunner(registry, store);

        // First run — pauses at `execute` because there's no pending response
        var runId = Guid.NewGuid().ToString();
        var paused = await runner.RunAsync(workflow, new WorkflowState { RunId = runId });

        Assert.Equal(WorkflowRunStatus.Interrupted, paused.Status);
        Assert.False(executeRan(), "execute should NOT run before resume");

        // Resume with the provided response
        var final = await runner.ResumeWithResponseAsync(workflow, runId, response);
        return (final, executeRan());
    }

    // ─── The matrix ─────────────────────────────────────────────────

    [Fact]
    public async Task Approved_RunsGuardedStep_AndCompletes()
    {
        var (state, executeRan) = await RunAndResumeAsync(
            InterruptResponse.ApprovedResponse(respondedBy: "alice"));

        Assert.Equal(WorkflowRunStatus.Completed, state.Status);
        Assert.Empty(state.Errors);
        Assert.True(executeRan, "execute should run after approval");
        Assert.True(state.Context.ContainsKey("execute"));
    }

    [Fact]
    public async Task Rejected_SkipsGuardedStep_AndFails()
    {
        var (state, executeRan) = await RunAndResumeAsync(
            InterruptResponse.RejectedResponse(respondedBy: "alice", comment: "too risky"));

        Assert.Equal(WorkflowRunStatus.Failed, state.Status);
        Assert.False(executeRan, "execute must NOT run after rejection");
        Assert.False(state.Context.ContainsKey("execute"));
        Assert.NotEmpty(state.Errors);
        Assert.Contains("rejected", state.Errors[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alice", state.Errors[0]);
        Assert.Contains("too risky", state.Errors[0]);
    }

    [Fact]
    public async Task TimedOut_SkipsGuardedStep_AndFails()
    {
        var (state, executeRan) = await RunAndResumeAsync(
            InterruptResponse.TimedOutResponse(comment: "no response in 30s"));

        Assert.Equal(WorkflowRunStatus.Failed, state.Status);
        Assert.False(executeRan, "execute must NOT run after timeout");
        Assert.False(state.Context.ContainsKey("execute"));
        Assert.NotEmpty(state.Errors);
        Assert.Contains("timed out", state.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancelled_SkipsGuardedStep_AndTerminatesCleanly()
    {
        var (state, executeRan) = await RunAndResumeAsync(
            InterruptResponse.CancelledResponse(comment: "operator aborted"));

        Assert.Equal(WorkflowRunStatus.Cancelled, state.Status);
        Assert.False(executeRan, "execute must NOT run after cancellation");
        Assert.False(state.Context.ContainsKey("execute"));
        // Cancellation is an intentional stop, not an error — Errors should stay empty.
        Assert.Empty(state.Errors);
    }

    // ─── Resume-after-cancel guard ──────────────────────────────────

    [Fact]
    public async Task ResumeAsync_OnCancelledRun_Throws()
    {
        var store = new InMemoryCheckpointStore();
        var registry = new InMemoryStepRegistry();
        var (workflow, _) = BuildGuardedWorkflow(registry);
        var runner = CreateRunner(registry, store);

        // Park at the interrupt, then cancel it
        var runId = Guid.NewGuid().ToString();
        await runner.RunAsync(workflow, new WorkflowState { RunId = runId });
        await runner.ResumeWithResponseAsync(
            workflow, runId, InterruptResponse.CancelledResponse());

        // Cancelled runs are terminal — ResumeAsync must refuse to restart them
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.ResumeAsync(workflow, runId));

        Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}