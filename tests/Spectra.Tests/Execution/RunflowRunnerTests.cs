using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Evaluation;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class WorkflowRunnerTests
{
    // ─── helpers ────────────────────────────────────────────────────

    private static WorkflowRunner CreateRunner(
        IStepRegistry registry,
        IStateMapper? stateMapper = null,
        IConditionEvaluator? conditionEvaluator = null,
        IEventSink? eventSink = null,
        ICheckpointStore? checkpointStore = null,
        IInterruptHandler? interruptHandler = null)
    {
        return new WorkflowRunner(
            registry,
            stateMapper ?? new StateMapper(),
            conditionEvaluator,
            eventSink,
            checkpointStore,
            interruptHandler: interruptHandler);
    }

    /// <summary>Inline step that always succeeds with configurable outputs.</summary>
    private sealed class LambdaStep : IStep
    {
        private readonly Func<StepContext, Task<StepResult>> _execute;

        public string StepType { get; }

        public LambdaStep(string stepType, Func<StepContext, Task<StepResult>> execute)
        {
            StepType = stepType;
            _execute = execute;
        }

        public LambdaStep(string stepType, Func<StepContext, StepResult> execute)
            : this(stepType, ctx => Task.FromResult(execute(ctx))) { }

        public Task<StepResult> ExecuteAsync(StepContext context) => _execute(context);
    }

    private sealed class InMemoryStepRegistry : IStepRegistry
    {
        private readonly Dictionary<string, IStep> _steps = new(StringComparer.OrdinalIgnoreCase);

        public IStep? GetStep(string stepType) =>
            _steps.GetValueOrDefault(stepType);

        public void Register(IStep step) =>
            _steps[step.StepType] = step;
    }

    private sealed class RecordingEventSink : IEventSink
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
            Task.FromResult(_store.Values
                .SelectMany(l => l)
                .Where(c => c.WorkflowId == workflowId)
                .OrderByDescending(c => c.UpdatedAt)
                .FirstOrDefault());

        public Task DeleteAsync(string runId, CancellationToken cancellationToken = default)
        {
            _store.Remove(runId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Checkpoint>> ListAsync(
            string? workflowId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Checkpoint>>(
                _store.Values
                    .SelectMany(l => l)
                    .Where(c => workflowId == null || c.WorkflowId == workflowId)
                    .ToList());

        public Task<Checkpoint?> LoadByIndexAsync(string runId, int index, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.TryGetValue(runId, out var list) && index >= 0 && index < list.Count
                ? list[index]
                : null);

        public Task<IReadOnlyList<Checkpoint>> ListByRunAsync(string runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Checkpoint>>(
                _store.TryGetValue(runId, out var list) ? list.ToList() : []);

        public Task<Checkpoint> ForkAsync(string sourceRunId, int checkpointIndex, string newRunId,
            WorkflowState? stateOverrides = null, CancellationToken cancellationToken = default)
        {
            if (!_store.TryGetValue(sourceRunId, out var srcList) || checkpointIndex < 0 || checkpointIndex >= srcList.Count)
                throw new InvalidOperationException($"Checkpoint index {checkpointIndex} not found for run '{sourceRunId}'.");

            var source = srcList[checkpointIndex];
            var clonedJson = System.Text.Json.JsonSerializer.Serialize(source.State);
            var cloned = System.Text.Json.JsonSerializer.Deserialize<WorkflowState>(clonedJson)!;
            if (stateOverrides != null)
            {
                foreach (var kvp in stateOverrides.Context) cloned.Context[kvp.Key] = kvp.Value;
                foreach (var kvp in stateOverrides.Inputs) cloned.Inputs[kvp.Key] = kvp.Value;
                foreach (var kvp in stateOverrides.Artifacts) cloned.Artifacts[kvp.Key] = kvp.Value;
            }
            cloned.RunId = newRunId;

            var forked = new Checkpoint
            {
                RunId = newRunId,
                WorkflowId = source.WorkflowId,
                State = cloned,
                LastCompletedNodeId = source.LastCompletedNodeId,
                NextNodeId = source.NextNodeId,
                StepsCompleted = source.StepsCompleted,
                SchemaVersion = source.SchemaVersion,
                Status = CheckpointStatus.InProgress,
                Index = 0,
                ParentRunId = sourceRunId,
                ParentCheckpointIndex = checkpointIndex,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _store[newRunId] = [forked];
            return Task.FromResult(forked);
        }

        public Task<IReadOnlyList<Checkpoint>> GetLineageAsync(string runId, CancellationToken cancellationToken = default)
        {
            var lineage = new List<Checkpoint>();
            var cur = runId;
            while (cur != null && _store.TryGetValue(cur, out var list) && list.Count > 0)
            {
                lineage.Add(list[0]);
                cur = list[0].ParentRunId;
            }
            lineage.Reverse();
            return Task.FromResult<IReadOnlyList<Checkpoint>>(lineage);
        }

        public Task PurgeAsync(string runId, CancellationToken cancellationToken = default)
        {
            _store.Remove(runId);
            return Task.CompletedTask;
        }

        public Checkpoint? LastSaved => _store.Values
            .SelectMany(l => l)
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefault();
    }

    // ─── sequential execution ───────────────────────────────────────

    [Fact]
    public async Task RunsSequentialWorkflow_ExecutesBothNodes()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("StepA", _ =>
            StepResult.Success(new() { ["a_out"] = "hello" })));
        registry.Register(new LambdaStep("StepB", _ =>
            StepResult.Success(new() { ["b_out"] = "world" })));

        var workflow = new WorkflowDefinition
        {
            Id = "seq-test",
            EntryNodeId = "nodeA",
            Nodes =
            [
                new NodeDefinition { Id = "nodeA", StepType = "StepA" },
                new NodeDefinition { Id = "nodeB", StepType = "StepB" }
            ],
            Edges = [new EdgeDefinition { From = "nodeA", To = "nodeB" }]
        };

        var runner = CreateRunner(registry);
        var state = await runner.RunAsync(workflow);

        Assert.Empty(state.Errors);
        Assert.True(state.Context.ContainsKey("nodeA"));
        Assert.True(state.Context.ContainsKey("nodeB"));
    }

    [Fact]
    public async Task UsesFirstNodeWhenEntryNodeIdIsNull()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Echo", _ =>
            StepResult.Success(new() { ["done"] = true })));

        var workflow = new WorkflowDefinition
        {
            Id = "no-entry",
            EntryNodeId = null,
            Nodes = [new NodeDefinition { Id = "only", StepType = "Echo" }],
            Edges = []
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        Assert.Empty(state.Errors);
        Assert.True(state.Context.ContainsKey("only"));
    }

    // ─── failure handling ───────────────────────────────────────────

    [Fact]
    public async Task StopsOnFailedStep()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Boom", _ =>
            StepResult.Fail("something broke")));
        registry.Register(new LambdaStep("Unreachable", _ =>
            StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "fail-test",
            EntryNodeId = "boom",
            Nodes =
            [
                new NodeDefinition { Id = "boom", StepType = "Boom" },
                new NodeDefinition { Id = "after", StepType = "Unreachable" }
            ],
            Edges = [new EdgeDefinition { From = "boom", To = "after" }]
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        Assert.Single(state.Errors);
        Assert.Contains("something broke", state.Errors[0]);
        Assert.Equal("boom", state.CurrentNodeId);
        Assert.False(state.Context.ContainsKey("after"));
    }

    [Fact]
    public async Task RecordsErrorWhenStepTypeNotInRegistry()
    {
        var registry = new InMemoryStepRegistry();
        // deliberately not registering "Missing"

        var workflow = new WorkflowDefinition
        {
            Id = "missing-step",
            EntryNodeId = "node1",
            Nodes = [new NodeDefinition { Id = "node1", StepType = "Missing" }],
            Edges = []
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        Assert.Single(state.Errors);
        Assert.Contains("Missing", state.Errors[0]);
    }

    [Fact]
    public async Task RecordsErrorWhenNodeIdNotFound()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("X", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "bad-edge",
            EntryNodeId = "start",
            Nodes =
            [
                new NodeDefinition { Id = "start", StepType = "X" }
            ],
            Edges = [new EdgeDefinition { From = "start", To = "ghost" }]
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        // It executes "start" fine, resolves next to "ghost", then fails lookup
        Assert.Single(state.Errors);
        Assert.Contains("ghost", state.Errors[0]);
    }

    // ─── state mapping ──────────────────────────────────────────────

    [Fact]
    public async Task PassesInputMappingsBetweenSteps()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Produce", _ =>
            StepResult.Success(new() { ["value"] = "fromA" })));
        registry.Register(new LambdaStep("Consume", ctx =>
            StepResult.Success(new() { ["received"] = ctx.Inputs["mapped"] })));

        var workflow = new WorkflowDefinition
        {
            Id = "mapping-test",
            EntryNodeId = "produce",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "produce",
                    StepType = "Produce",
                    OutputMappings = new() { ["value"] = "Context.Shared" }
                },
                new NodeDefinition
                {
                    Id = "consume",
                    StepType = "Consume",
                    InputMappings = new() { ["mapped"] = "Context.Shared" }
                }
            ],
            Edges = [new EdgeDefinition { From = "produce", To = "consume" }]
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        Assert.Empty(state.Errors);
        var consumeOutput = state.Context["consume"] as Dictionary<string, object?>;
        Assert.NotNull(consumeOutput);
        Assert.Equal("fromA", consumeOutput["received"]);
    }

    [Fact]
    public async Task DefaultOutputMapping_StoresUnderContextNodeId()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Emit", _ =>
            StepResult.Success(new() { ["x"] = 42 })));

        var workflow = new WorkflowDefinition
        {
            Id = "default-output",
            EntryNodeId = "emit",
            Nodes = [new NodeDefinition { Id = "emit", StepType = "Emit" }],
            Edges = []
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        Assert.True(state.Context.ContainsKey("emit"));
        var outputs = state.Context["emit"] as Dictionary<string, object?>;
        Assert.NotNull(outputs);
        Assert.Equal(42, outputs["x"]);
    }

    [Fact]
    public async Task InitialStateInputsAreAccessible()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Reader", ctx =>
            StepResult.Success(new() { ["got"] = ctx.Inputs["val"] })));

        var workflow = new WorkflowDefinition
        {
            Id = "init-state",
            EntryNodeId = "read",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "read",
                    StepType = "Reader",
                    InputMappings = new() { ["val"] = "Inputs.seed" }
                }
            ],
            Edges = []
        };

        var initialState = new WorkflowState
        {
            Inputs = new() { ["seed"] = "abc" }
        };

        var state = await CreateRunner(registry).RunAsync(workflow, initialState);

        Assert.Empty(state.Errors);
        var output = state.Context["read"] as Dictionary<string, object?>;
        Assert.Equal("abc", output?["got"]);
    }

    // ─── conditional branching ──────────────────────────────────────

    [Fact]
    public async Task FollowsConditionalEdge_WhenConditionSatisfied()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("SetFlag", _ =>
            StepResult.Success(new() { ["flag"] = true })));
        registry.Register(new LambdaStep("BranchA", _ =>
            StepResult.Success(new() { ["branch"] = "A" })));
        registry.Register(new LambdaStep("BranchB", _ =>
            StepResult.Success(new() { ["branch"] = "B" })));

        var workflow = new WorkflowDefinition
        {
            Id = "branch-test",
            EntryNodeId = "set",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "set",
                    StepType = "SetFlag",
                    OutputMappings = new() { ["flag"] = "Context.Flag" }
                },
                new NodeDefinition { Id = "a", StepType = "BranchA" },
                new NodeDefinition { Id = "b", StepType = "BranchB" }
            ],
            Edges =
            [
                new EdgeDefinition { From = "set", To = "a", Condition = "Context.Flag == true" },
                new EdgeDefinition { From = "set", To = "b" }
            ]
        };

        var evaluator = new Spectra.Kernel.Evaluation.SimpleConditionEvaluator();
        var runner = CreateRunner(registry, conditionEvaluator: evaluator);
        var state = await runner.RunAsync(workflow);

        Assert.Empty(state.Errors);
        Assert.True(state.Context.ContainsKey("a"));
        Assert.False(state.Context.ContainsKey("b"));
    }

    [Fact]
    public async Task FallsBackToUnconditionalEdge()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Noop", _ => StepResult.Success()));
        registry.Register(new LambdaStep("Fallback", _ =>
            StepResult.Success(new() { ["fb"] = true })));

        var workflow = new WorkflowDefinition
        {
            Id = "fallback-test",
            EntryNodeId = "start",
            Nodes =
            [
                new NodeDefinition { Id = "start", StepType = "Noop" },
                new NodeDefinition { Id = "fb", StepType = "Fallback" }
            ],
            Edges =
            [
                new EdgeDefinition
                {
                    From = "start",
                    To = "fb",
                    Condition = null // unconditional
                }
            ]
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        Assert.Empty(state.Errors);
        Assert.True(state.Context.ContainsKey("fb"));
    }

    // ─── infinite loop detection ────────────────────────────────────

    [Fact]
    public async Task DetectsCycleAtValidation()
    {
        var callCount = 0;
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Loop", _ =>
        {
            callCount++;
            return StepResult.Success();
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "loop-test",
            EntryNodeId = "a",
            Nodes =
            [
                new NodeDefinition { Id = "a", StepType = "Loop" },
                new NodeDefinition { Id = "b", StepType = "Loop" }
            ],
            Edges =
            [
                new EdgeDefinition { From = "a", To = "b" },
                new EdgeDefinition { From = "b", To = "a" }
            ]
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        Assert.NotEmpty(state.Errors);
        Assert.Contains(state.Errors, e => e.Contains("cycle"));
    }

    // ─── loopback / cycle support ─────────────────────────────────

    [Fact]
    public async Task LoopbackEdge_ExecutesNodeMultipleTimes()
    {
        var callCount = 0;
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Counter", _ =>
        {
            callCount++;
            return StepResult.Success(new() { ["count"] = callCount });
        }));

        var evaluator = new Spectra.Kernel.Evaluation.SimpleConditionEvaluator();

        var workflow = new WorkflowDefinition
        {
            Id = "loopback-test",
            EntryNodeId = "a",
            MaxNodeIterations = 5,
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "a",
                    StepType = "Counter",
                    OutputMappings = new() { ["count"] = "Context.Count" }
                }
            ],
            Edges =
            [
                new EdgeDefinition
                {
                    From = "a",
                    To = "a",
                    IsLoopback = true,
                    Condition = "Context.Count < 3"
                }
            ]
        };

        var runner = CreateRunner(registry, conditionEvaluator: evaluator);
        var state = await runner.RunAsync(workflow);

        Assert.Empty(state.Errors);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task LoopbackEdge_RespectsMaxIterations()
    {
        var callCount = 0;
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Forever", _ =>
        {
            callCount++;
            return StepResult.Success();
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "max-iter-test",
            EntryNodeId = "a",
            MaxNodeIterations = 5,
            Nodes =
            [
                new NodeDefinition { Id = "a", StepType = "Forever" }
            ],
            Edges =
            [
                new EdgeDefinition { From = "a", To = "a", IsLoopback = true }
            ]
        };

        var runner = CreateRunner(registry);
        var state = await runner.RunAsync(workflow);

        Assert.NotEmpty(state.Errors);
        Assert.Contains(state.Errors, e => e.Contains("exceeded maximum iterations"));
        Assert.True(callCount <= 6); // initial + up to MaxNodeIterations
    }

    [Fact]
    public async Task LoopbackEdge_TwoNodeCycle()
    {
        var aCount = 0;
        var bCount = 0;
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("StepA", _ =>
        {
            aCount++;
            return StepResult.Success(new() { ["total"] = aCount + bCount });
        }));
        registry.Register(new LambdaStep("StepB", _ =>
        {
            bCount++;
            return StepResult.Success(new() { ["total"] = aCount + bCount });
        }));

        var evaluator = new Spectra.Kernel.Evaluation.SimpleConditionEvaluator();

        var workflow = new WorkflowDefinition
        {
            Id = "two-node-loop",
            EntryNodeId = "a",
            MaxNodeIterations = 10,
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "a",
                    StepType = "StepA",
                    OutputMappings = new() { ["total"] = "Context.Total" }
                },
                new NodeDefinition
                {
                    Id = "b",
                    StepType = "StepB",
                    OutputMappings = new() { ["total"] = "Context.Total" }
                }
            ],
            Edges =
            [
                new EdgeDefinition { From = "a", To = "b" },
                new EdgeDefinition { From = "b", To = "a", IsLoopback = true, Condition = "Context.Total < 4" }
            ]
        };

        var runner = CreateRunner(registry, conditionEvaluator: evaluator);
        var state = await runner.RunAsync(workflow);

        Assert.Empty(state.Errors);
        // a runs, b runs (total=2), loop back, a runs, b runs (total=4), condition false, done
        Assert.Equal(2, aCount);
        Assert.Equal(2, bCount);
    }

    // ─── NeedsContinuation ──────────────────────────────────────────

    [Fact]
    public async Task NeedsContinuation_BreaksAndSavesCheckpoint()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Pause", _ =>
            StepResult.NeedsContinuation("waiting for external data")));

        var checkpointStore = new InMemoryCheckpointStore();

        var workflow = new WorkflowDefinition
        {
            Id = "continuation-test",
            EntryNodeId = "pause",
            Nodes =
            [
                new NodeDefinition { Id = "pause", StepType = "Pause" },
                new NodeDefinition { Id = "after", StepType = "Pause" }
            ],
            Edges = [new EdgeDefinition { From = "pause", To = "after" }]
        };

        var runner = CreateRunner(registry, checkpointStore: checkpointStore);
        var state = await runner.RunAsync(workflow);

        Assert.Empty(state.Errors);

        var checkpoint = checkpointStore.LastSaved;
        Assert.NotNull(checkpoint);
        Assert.Equal(CheckpointStatus.InProgress, checkpoint.Status);
        Assert.Equal("pause", checkpoint.NextNodeId);
    }

    // ─── events ─────────────────────────────────────────────────────

    [Fact]
    public async Task EmitsWorkflowStartedAndCompletedEvents()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Ok", _ => StepResult.Success()));

        var sink = new RecordingEventSink();

        var workflow = new WorkflowDefinition
        {
            Id = "events-test",
            EntryNodeId = "ok",
            Nodes = [new NodeDefinition { Id = "ok", StepType = "Ok" }],
            Edges = []
        };

        var runner = CreateRunner(registry, eventSink: sink);
        await runner.RunAsync(workflow);

        Assert.Contains(sink.Events, e => e is WorkflowStartedEvent);
        Assert.Contains(sink.Events, e => e is WorkflowCompletedEvent);
    }

    [Fact]
    public async Task EmitsStepStartedAndCompletedEvents()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Tracked", _ => StepResult.Success()));

        var sink = new RecordingEventSink();

        var workflow = new WorkflowDefinition
        {
            Id = "step-events",
            EntryNodeId = "t",
            Nodes = [new NodeDefinition { Id = "t", StepType = "Tracked" }],
            Edges = []
        };

        await CreateRunner(registry, eventSink: sink).RunAsync(workflow);

        Assert.Contains(sink.Events, e => e is StepStartedEvent s && s.NodeId == "t");
        Assert.Contains(sink.Events, e => e is StepCompletedEvent c &&
            c.NodeId == "t" && c.Status == StepStatus.Succeeded);
    }

    [Fact]
    public async Task EmitsStateChangedEvent_ForOutputMappings()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Writer", _ =>
            StepResult.Success(new() { ["val"] = "data" })));

        var sink = new RecordingEventSink();

        var workflow = new WorkflowDefinition
        {
            Id = "state-events",
            EntryNodeId = "w",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "w",
                    StepType = "Writer",
                    OutputMappings = new() { ["val"] = "Context.Result" }
                }
            ],
            Edges = []
        };

        await CreateRunner(registry, eventSink: sink).RunAsync(workflow);

        Assert.Contains(sink.Events, e => e is StateChangedEvent sc &&
            sc.Section == "Context" && sc.Key == "Result");
    }

    [Fact]
    public async Task CompletedEvent_ReportsSuccessFalseOnFailure()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Fail", _ =>
            StepResult.Fail("oops")));

        var sink = new RecordingEventSink();

        var workflow = new WorkflowDefinition
        {
            Id = "fail-event",
            EntryNodeId = "f",
            Nodes = [new NodeDefinition { Id = "f", StepType = "Fail" }],
            Edges = []
        };

        await CreateRunner(registry, eventSink: sink).RunAsync(workflow);

        var completed = sink.Events.OfType<WorkflowCompletedEvent>().Single();
        Assert.False(completed.Success);
        Assert.NotEmpty(completed.Errors);
    }

    // ─── checkpointing ──────────────────────────────────────────────

    [Fact]
    public async Task CheckpointIsSaved_OnSuccessfulCompletion()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Ok", _ => StepResult.Success()));

        var store = new InMemoryCheckpointStore();

        var workflow = new WorkflowDefinition
        {
            Id = "cp-success",
            EntryNodeId = "ok",
            Nodes = [new NodeDefinition { Id = "ok", StepType = "Ok" }],
            Edges = []
        };

        var runner = CreateRunner(registry, checkpointStore: store);
        var state = await runner.RunAsync(workflow);

        var cp = await store.LoadAsync(state.RunId);
        Assert.NotNull(cp);
        Assert.Equal(CheckpointStatus.Completed, cp.Status);
    }

    [Fact]
    public async Task CheckpointIsSaved_OnFailure()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Bad", _ =>
            StepResult.Fail("broke")));

        var store = new InMemoryCheckpointStore();

        var workflow = new WorkflowDefinition
        {
            Id = "cp-fail",
            EntryNodeId = "bad",
            Nodes = [new NodeDefinition { Id = "bad", StepType = "Bad" }],
            Edges = []
        };

        var state = await CreateRunner(registry, checkpointStore: store).RunAsync(workflow);

        var cp = await store.LoadAsync(state.RunId);
        Assert.NotNull(cp);
        Assert.Equal(CheckpointStatus.Failed, cp.Status);
    }

    // ─── cancellation ───────────────────────────────────────────────

    [Fact]
    public async Task ThrowsOnCancellation()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Slow", async ctx =>
        {
            await Task.Delay(5000, ctx.CancellationToken);
            return StepResult.Success();
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "cancel-test",
            EntryNodeId = "slow",
            Nodes = [new NodeDefinition { Id = "slow", StepType = "Slow" }],
            Edges = []
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateRunner(registry).RunAsync(workflow, cancellationToken: cts.Token));
    }

    // ─── interrupt handler ──────────────────────────────────────────

    [Fact]
    public async Task InterruptHandler_IsWiredIntoStepContext()
    {
        InterruptRequest? captured = null;

        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Gated", async ctx =>
        {
            var response = await ctx.InterruptAsync(new InterruptRequest
            {
                RunId = ctx.RunId,
                WorkflowId = ctx.WorkflowId,
                NodeId = ctx.NodeId,
                Reason = "need approval"
            });

            return response.Approved
                ? StepResult.Success(new() { ["approved"] = true })
                : StepResult.Fail("rejected");
        }));

        var handler = new LambdaInterruptHandler(req =>
        {
            captured = req;
            return Task.FromResult(InterruptResponse.ApprovedResponse());
        });

        var workflow = new WorkflowDefinition
        {
            Id = "interrupt-test",
            EntryNodeId = "gated",
            Nodes = [new NodeDefinition { Id = "gated", StepType = "Gated" }],
            Edges = []
        };

        var runner = CreateRunner(registry, interruptHandler: handler);
        var state = await runner.RunAsync(workflow);

        Assert.Empty(state.Errors);
        Assert.NotNull(captured);
        Assert.Equal("need approval", captured.Reason);
    }

    [Fact]
    public async Task InterruptHandler_RejectionCausesFailure()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Gated", async ctx =>
        {
            var response = await ctx.InterruptAsync(new InterruptRequest
            {
                RunId = ctx.RunId,
                WorkflowId = ctx.WorkflowId,
                NodeId = ctx.NodeId,
                Reason = "need approval"
            });

            return response.Approved
                ? StepResult.Success()
                : StepResult.Fail("rejected by handler");
        }));

        var handler = new LambdaInterruptHandler(_ =>
            Task.FromResult(InterruptResponse.RejectedResponse(comment: "nope")));

        var workflow = new WorkflowDefinition
        {
            Id = "reject-test",
            EntryNodeId = "gated",
            Nodes = [new NodeDefinition { Id = "gated", StepType = "Gated" }],
            Edges = []
        };

        var state = await CreateRunner(registry, interruptHandler: handler).RunAsync(workflow);

        Assert.Single(state.Errors);
        Assert.Contains("rejected", state.Errors[0]);
    }

    [Fact]
    public async Task NoInterruptHandler_StepInterruptSuspendsWorkflow()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Gated", async ctx =>
        {
            await ctx.InterruptAsync(new InterruptRequest
            {
                RunId = ctx.RunId,
                WorkflowId = ctx.WorkflowId,
                NodeId = ctx.NodeId,
                Reason = "need approval"
            });
            return StepResult.Success();
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "no-handler",
            EntryNodeId = "gated",
            Nodes = [new NodeDefinition { Id = "gated", StepType = "Gated" }],
            Edges = []
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        // Workflow should suspend, not crash
        Assert.Equal("gated", state.CurrentNodeId);
        Assert.Empty(state.Errors);
    }

    // ─── agentId passthrough ────────────────────────────────────────

    [Fact]
    public async Task AgentId_InjectedIntoInputsWhenSetOnNode()
    {
        string? receivedAgentId = null;

        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("AgentStep", ctx =>
        {
            receivedAgentId = ctx.Inputs.GetValueOrDefault("agentId") as string;
            return StepResult.Success();
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "agent-test",
            EntryNodeId = "a",
            Agents =
            [
                new AgentDefinition
                {
                    Id = "claude-3",
                    Provider = "anthropic",
                    Model = "claude-3"
                }
            ],
            Nodes =
            [
                new NodeDefinition { Id = "a", StepType = "AgentStep", AgentId = "claude-3" }
            ],
            Edges = []
        };

        await CreateRunner(registry).RunAsync(workflow);

        Assert.Equal("claude-3", receivedAgentId);
    }

    // ─── multi-step pipeline ────────────────────────────────────────

    [Fact]
    public async Task ThreeStepPipeline_AllExecuteInOrder()
    {
        var executionOrder = new List<string>();

        var registry = new InMemoryStepRegistry();
        foreach (var name in new[] { "A", "B", "C" })
        {
            var captured = name;
            registry.Register(new LambdaStep(captured, _ =>
            {
                executionOrder.Add(captured);
                return StepResult.Success();
            }));
        }

        var workflow = new WorkflowDefinition
        {
            Id = "pipeline",
            EntryNodeId = "n1",
            Nodes =
            [
                new NodeDefinition { Id = "n1", StepType = "A" },
                new NodeDefinition { Id = "n2", StepType = "B" },
                new NodeDefinition { Id = "n3", StepType = "C" }
            ],
            Edges =
            [
                new EdgeDefinition { From = "n1", To = "n2" },
                new EdgeDefinition { From = "n2", To = "n3" }
            ]
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        Assert.Empty(state.Errors);
        Assert.Equal(["A", "B", "C"], executionOrder);
    }

    // ─── resume ─────────────────────────────────────────────────────

    [Fact]
    public async Task ResumeAsync_ThrowsWithoutCheckpointStore()
    {
        var registry = new InMemoryStepRegistry();
        var runner = CreateRunner(registry);

        var workflow = new WorkflowDefinition { Id = "w" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.ResumeAsync(workflow, "nonexistent"));
    }

    [Fact]
    public async Task ResumeAsync_ThrowsWhenNoCheckpointExists()
    {
        var registry = new InMemoryStepRegistry();
        var store = new InMemoryCheckpointStore();
        var runner = CreateRunner(registry, checkpointStore: store);

        var workflow = new WorkflowDefinition { Id = "w" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.ResumeAsync(workflow, "missing-run"));
    }

    // ─── workflow sets WorkflowId on state ──────────────────────────

    [Fact]
    public async Task SetsWorkflowIdOnState()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Noop", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "my-wf",
            EntryNodeId = "n",
            Nodes = [new NodeDefinition { Id = "n", StepType = "Noop" }],
            Edges = []
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        Assert.Equal("my-wf", state.WorkflowId);
    }

    // ─── no edges = single node workflow ────────────────────────────

    [Fact]
    public async Task SetsRunStatus_ToCompleted_OnSuccess()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Noop", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "status-ok",
            EntryNodeId = "n",
            Nodes = [new NodeDefinition { Id = "n", StepType = "Noop" }],
            Edges = []
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        Assert.Equal(WorkflowRunStatus.Completed, state.Status);
    }

    [Fact]
    public async Task SetsRunStatus_ToFailed_OnStepFailure()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Bad", _ => StepResult.Fail("broke")));

        var workflow = new WorkflowDefinition
        {
            Id = "status-fail",
            EntryNodeId = "b",
            Nodes = [new NodeDefinition { Id = "b", StepType = "Bad" }],
            Edges = []
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        Assert.Equal(WorkflowRunStatus.Failed, state.Status);
    }

    [Fact]
    public async Task SingleNodeWorkflow_Completes()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Solo", _ =>
            StepResult.Success(new() { ["result"] = "done" })));

        var workflow = new WorkflowDefinition
        {
            Id = "single",
            EntryNodeId = "solo",
            Nodes = [new NodeDefinition { Id = "solo", StepType = "Solo" }],
            Edges = []
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        Assert.Empty(state.Errors);
        Assert.True(state.Context.ContainsKey("solo"));
    }

    // ─── test helper ────────────────────────────────────────────────

    private sealed class LambdaInterruptHandler : IInterruptHandler
    {
        private readonly Func<InterruptRequest, Task<InterruptResponse>> _handle;

        public LambdaInterruptHandler(Func<InterruptRequest, Task<InterruptResponse>> handle)
            => _handle = handle;

        public Task<InterruptResponse> HandleAsync(
            InterruptRequest request,
            CancellationToken cancellationToken = default)
            => _handle(request);
    }
}