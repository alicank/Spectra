using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class RunContextPropagationTests
{
    // ─── helpers (same pattern as WorkflowRunnerTests) ───────────────

    private sealed class InMemoryStepRegistry : IStepRegistry
    {
        private readonly Dictionary<string, IStep> _steps = new(StringComparer.OrdinalIgnoreCase);

        public IStep? GetStep(string stepType) =>
            _steps.GetValueOrDefault(stepType);

        public void Register(IStep step) =>
            _steps[step.StepType] = step;
    }

    private sealed class CapturingEventSink : IEventSink
    {
        public List<WorkflowEvent> Events { get; } = [];

        public Task PublishAsync(WorkflowEvent evt, CancellationToken cancellationToken = default)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }
    }

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
            var indexed = checkpoint with { Index = list.Count };
            list.Add(indexed);
            return Task.CompletedTask;
        }

        public Task<Checkpoint?> LoadAsync(string runId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.TryGetValue(runId, out var list) && list.Count > 0
                ? list[^1]
                : null);

        public Task<Checkpoint?> LoadLatestAsync(string workflowId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Checkpoint?>(null);

        public Task DeleteAsync(string runId, CancellationToken cancellationToken = default)
        {
            _store.Remove(runId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Checkpoint>> ListAsync(
            string? workflowId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Checkpoint>>([]);

        public Task<Checkpoint?> LoadByIndexAsync(string runId, int index, CancellationToken cancellationToken = default) =>
            Task.FromResult<Checkpoint?>(null);

        public Task<IReadOnlyList<Checkpoint>> ListByRunAsync(string runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Checkpoint>>(
                _store.TryGetValue(runId, out var list) ? list.ToList() : []);

        public Task<Checkpoint> ForkAsync(string sourceRunId, int checkpointIndex, string newRunId,
            WorkflowState? stateOverrides = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<Checkpoint>> GetLineageAsync(string runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Checkpoint>>([]);

        public Task PurgeAsync(string runId, CancellationToken cancellationToken = default)
        {
            _store.Remove(runId);
            return Task.CompletedTask;
        }
    }

    private sealed class EchoStep : IStep
    {
        public string StepType => "echo";

        public Task<StepResult> ExecuteAsync(StepContext context)
        {
            return Task.FromResult(StepResult.Success(new Dictionary<string, object?>
            {
                ["response"] = "echo"
            }));
        }
    }

    /// <summary>
    /// Step that captures the RunContext it receives, for assertion.
    /// </summary>
    private sealed class ContextCapturingStep : IStep
    {
        public string StepType => "capture";
        public RunContext? CapturedContext { get; private set; }

        public Task<StepResult> ExecuteAsync(StepContext context)
        {
            CapturedContext = context.RunContext;
            return Task.FromResult(StepResult.Success(new Dictionary<string, object?>
            {
                ["tenantId"] = context.RunContext.TenantId,
                ["userId"] = context.RunContext.UserId
            }));
        }
    }

    // ─── event identity stamping ────────────────────────────────────

    /// <summary>
    /// Verifies that WorkflowRunner stamps TenantId/UserId from RunContext onto emitted events.
    /// </summary>
    [Fact]
    public async Task Events_are_stamped_with_runcontext_identity()
    {
        var sink = new CapturingEventSink();

        var registry = new InMemoryStepRegistry();
        registry.Register(new EchoStep());

        var runner = new WorkflowRunner(
            registry,
            new StateMapper(),
            eventSink: sink);

        var workflow = new WorkflowDefinition
        {
            Id = "identity-events",
            EntryNodeId = "echo",
            Nodes = [new NodeDefinition { Id = "echo", StepType = "echo" }],
            Edges = []
        };

        var runContext = new RunContext
        {
            TenantId = "tenant-1",
            UserId = "user-42"
        };

        await runner.RunAsync(workflow, null, runContext);

        Assert.NotEmpty(sink.Events);
        Assert.All(sink.Events, evt =>
        {
            Assert.Equal("tenant-1", evt.TenantId);
            Assert.Equal("user-42", evt.UserId);
        });
    }

    // ─── checkpoint identity stamping ───────────────────────────────

    /// <summary>
    /// Verifies that checkpoints include TenantId and UserId from RunContext.
    /// </summary>
    [Fact]
    public async Task Checkpoints_carry_identity_metadata()
    {
        var checkpointStore = new InMemoryCheckpointStore();

        var registry = new InMemoryStepRegistry();
        registry.Register(new EchoStep());

        var runner = new WorkflowRunner(
            registry,
            new StateMapper(),
            checkpointStore: checkpointStore,
            checkpointOptions: new CheckpointOptions
            {
                Frequency = CheckpointFrequency.EveryNode
            });

        var workflow = new WorkflowDefinition
        {
            Id = "identity-checkpoint",
            EntryNodeId = "echo",
            Nodes = [new NodeDefinition { Id = "echo", StepType = "echo" }],
            Edges = []
        };

        var runContext = new RunContext
        {
            TenantId = "acme-corp",
            UserId = "admin-1"
        };

        var state = await runner.RunAsync(workflow, null, runContext);

        var checkpoint = await checkpointStore.LoadAsync(state.RunId);
        Assert.NotNull(checkpoint);
        Assert.Equal("acme-corp", checkpoint.TenantId);
        Assert.Equal("admin-1", checkpoint.UserId);
    }

    // ─── RunContext propagation through StepContext ──────────────────

    /// <summary>
    /// Verifies that each step receives the RunContext via StepContext.RunContext.
    /// </summary>
    [Fact]
    public async Task StepContext_receives_runcontext()
    {
        var captureStep = new ContextCapturingStep();

        var registry = new InMemoryStepRegistry();
        registry.Register(captureStep);

        var runner = new WorkflowRunner(
            registry,
            new StateMapper());

        var workflow = new WorkflowDefinition
        {
            Id = "step-context-test",
            EntryNodeId = "cap",
            Nodes = [new NodeDefinition { Id = "cap", StepType = "capture" }],
            Edges = []
        };

        var runContext = new RunContext
        {
            TenantId = "t-99",
            UserId = "u-7",
            Roles = ["admin", "viewer"]
        };

        await runner.RunAsync(workflow, null, runContext);

        Assert.NotNull(captureStep.CapturedContext);
        Assert.Equal("t-99", captureStep.CapturedContext.TenantId);
        Assert.Equal("u-7", captureStep.CapturedContext.UserId);
        Assert.Contains("admin", captureStep.CapturedContext.Roles);
        Assert.Contains("viewer", captureStep.CapturedContext.Roles);
    }

    // ─── anonymous context ──────────────────────────────────────────

    /// <summary>
    /// Verifies that when no RunContext is provided, Anonymous is used
    /// and events have null identity.
    /// </summary>
    [Fact]
    public async Task No_runcontext_uses_anonymous_identity()
    {
        var sink = new CapturingEventSink();

        var registry = new InMemoryStepRegistry();
        registry.Register(new EchoStep());

        var runner = new WorkflowRunner(
            registry,
            new StateMapper(),
            eventSink: sink);

        var workflow = new WorkflowDefinition
        {
            Id = "anonymous-test",
            EntryNodeId = "echo",
            Nodes = [new NodeDefinition { Id = "echo", StepType = "echo" }],
            Edges = []
        };

        // No RunContext overload — should use Anonymous
        await runner.RunAsync(workflow);

        Assert.NotEmpty(sink.Events);
        Assert.All(sink.Events, evt =>
        {
            Assert.Null(evt.TenantId);
            Assert.Null(evt.UserId);
        });
    }

    // ─── multi-step identity consistency ────────────────────────────

    /// <summary>
    /// Verifies that identity is consistent across multiple steps in a pipeline.
    /// </summary>
    [Fact]
    public async Task Identity_is_consistent_across_pipeline_steps()
    {
        var sink = new CapturingEventSink();

        var registry = new InMemoryStepRegistry();
        registry.Register(new EchoStep());

        // Register a second step type reusing EchoStep logic
        registry.Register(new LambdaStep("step2", _ =>
            StepResult.Success(new Dictionary<string, object?> { ["ok"] = true })));

        var runner = new WorkflowRunner(
            registry,
            new StateMapper(),
            eventSink: sink);

        var workflow = new WorkflowDefinition
        {
            Id = "pipeline-identity",
            EntryNodeId = "first",
            Nodes =
            [
                new NodeDefinition { Id = "first", StepType = "echo" },
                new NodeDefinition { Id = "second", StepType = "step2" }
            ],
            Edges = [new EdgeDefinition { From = "first", To = "second" }]
        };

        var runContext = new RunContext
        {
            TenantId = "multi-tenant",
            UserId = "pipeline-user"
        };

        await runner.RunAsync(workflow, null, runContext);

        // All step events should carry identity
        var stepEvents = sink.Events
            .Where(e => e is StepStartedEvent or StepCompletedEvent)
            .ToList();

        Assert.True(stepEvents.Count >= 4); // 2 started + 2 completed

        Assert.All(stepEvents, evt =>
        {
            Assert.Equal("multi-tenant", evt.TenantId);
            Assert.Equal("pipeline-user", evt.UserId);
        });
    }

    // ─── inline helpers ─────────────────────────────────────────────

    private sealed class LambdaStep : IStep
    {
        private readonly Func<StepContext, StepResult> _execute;
        public string StepType { get; }

        public LambdaStep(string stepType, Func<StepContext, StepResult> execute)
        {
            StepType = stepType;
            _execute = execute;
        }

        public Task<StepResult> ExecuteAsync(StepContext context) =>
            Task.FromResult(_execute(context));
    }
}