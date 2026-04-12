using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Evaluation;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Streaming;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class WorkflowStreamingTests
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

    // ─── StreamAsync basics ─────────────────────────────────────────

    [Fact]
    public async Task StreamAsync_YieldsWorkflowStartedAndCompleted()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Ok", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "stream-basic",
            EntryNodeId = "ok",
            Nodes = [new NodeDefinition { Id = "ok", StepType = "Ok" }],
            Edges = []
        };

        var runner = CreateRunner(registry);
        var events = new List<WorkflowEvent>();

        await foreach (var evt in runner.StreamAsync(workflow))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is WorkflowStartedEvent);
        Assert.Contains(events, e => e is WorkflowCompletedEvent);
    }

    [Fact]
    public async Task StreamAsync_YieldsStepStartedAndCompleted()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Tracked", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "stream-steps",
            EntryNodeId = "t",
            Nodes = [new NodeDefinition { Id = "t", StepType = "Tracked" }],
            Edges = []
        };

        var runner = CreateRunner(registry);
        var events = new List<WorkflowEvent>();

        await foreach (var evt in runner.StreamAsync(workflow))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is StepStartedEvent s && s.NodeId == "t");
        Assert.Contains(events, e => e is StepCompletedEvent c &&
            c.NodeId == "t" && c.Status == StepStatus.Succeeded);
    }

    // ─── token streaming ────────────────────────────────────────────

    [Fact]
    public async Task StreamAsync_StepEmitsTokens_YieldsTokenStreamEvents()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Streamer", async ctx =>
        {
            if (ctx.OnToken != null)
            {
                await ctx.OnToken("Hello", ctx.CancellationToken);
                await ctx.OnToken(" ", ctx.CancellationToken);
                await ctx.OnToken("World", ctx.CancellationToken);
            }
            return StepResult.Success(new() { ["response"] = "Hello World" });
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "token-stream",
            EntryNodeId = "llm",
            Nodes = [new NodeDefinition { Id = "llm", StepType = "Streamer" }],
            Edges = []
        };

        var runner = CreateRunner(registry);
        var tokenEvents = new List<TokenStreamEvent>();

        await foreach (var evt in runner.StreamAsync(workflow))
        {
            if (evt is TokenStreamEvent te)
                tokenEvents.Add(te);
        }

        Assert.Equal(3, tokenEvents.Count);
        Assert.Equal("Hello", tokenEvents[0].Token);
        Assert.Equal(" ", tokenEvents[1].Token);
        Assert.Equal("World", tokenEvents[2].Token);

        // Verify sequential token indices
        Assert.Equal(0, tokenEvents[0].TokenIndex);
        Assert.Equal(1, tokenEvents[1].TokenIndex);
        Assert.Equal(2, tokenEvents[2].TokenIndex);

        // All tokens belong to the correct node
        Assert.All(tokenEvents, te => Assert.Equal("llm", te.NodeId));
    }

    [Fact]
    public async Task StreamAsync_TokensFromMultipleNodes_HaveCorrectNodeIds()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("StreamA", async ctx =>
        {
            if (ctx.OnToken != null)
            {
                await ctx.OnToken("A1", ctx.CancellationToken);
                await ctx.OnToken("A2", ctx.CancellationToken);
            }
            return StepResult.Success(new() { ["out"] = "a" });
        }));
        registry.Register(new LambdaStep("StreamB", async ctx =>
        {
            if (ctx.OnToken != null)
            {
                await ctx.OnToken("B1", ctx.CancellationToken);
            }
            return StepResult.Success(new() { ["out"] = "b" });
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "multi-node-tokens",
            EntryNodeId = "a",
            Nodes =
            [
                new NodeDefinition { Id = "a", StepType = "StreamA" },
                new NodeDefinition { Id = "b", StepType = "StreamB" }
            ],
            Edges = [new EdgeDefinition { From = "a", To = "b" }]
        };

        var runner = CreateRunner(registry);
        var tokenEvents = new List<TokenStreamEvent>();

        await foreach (var evt in runner.StreamAsync(workflow))
        {
            if (evt is TokenStreamEvent te)
                tokenEvents.Add(te);
        }

        // 2 from node A, 1 from node B
        Assert.Equal(3, tokenEvents.Count);

        var aTokens = tokenEvents.Where(t => t.NodeId == "a").ToList();
        var bTokens = tokenEvents.Where(t => t.NodeId == "b").ToList();

        Assert.Equal(2, aTokens.Count);
        Assert.Single(bTokens);

        // Each node resets token index
        Assert.Equal(0, aTokens[0].TokenIndex);
        Assert.Equal(1, aTokens[1].TokenIndex);
        Assert.Equal(0, bTokens[0].TokenIndex);
    }

    [Fact]
    public async Task StreamAsync_NonStreamingStep_NoTokenEvents()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Plain", _ =>
            StepResult.Success(new() { ["x"] = 1 })));

        var workflow = new WorkflowDefinition
        {
            Id = "no-tokens",
            EntryNodeId = "p",
            Nodes = [new NodeDefinition { Id = "p", StepType = "Plain" }],
            Edges = []
        };

        var runner = CreateRunner(registry);
        var tokenEvents = new List<TokenStreamEvent>();

        await foreach (var evt in runner.StreamAsync(workflow))
        {
            if (evt is TokenStreamEvent te)
                tokenEvents.Add(te);
        }

        Assert.Empty(tokenEvents);
    }

    // ─── StepContext.IsStreaming ─────────────────────────────────────

    [Fact]
    public async Task StreamAsync_StepContext_IsStreamingIsTrue()
    {
        bool? wasStreaming = null;

        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Check", ctx =>
        {
            wasStreaming = ctx.IsStreaming;
            return StepResult.Success();
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "is-streaming",
            EntryNodeId = "c",
            Nodes = [new NodeDefinition { Id = "c", StepType = "Check" }],
            Edges = []
        };

        var runner = CreateRunner(registry);

        await foreach (var _ in runner.StreamAsync(workflow)) { }

        Assert.True(wasStreaming);
    }

    [Fact]
    public async Task RunAsync_StepContext_IsStreamingIsFalse()
    {
        bool? wasStreaming = null;

        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Check", ctx =>
        {
            wasStreaming = ctx.IsStreaming;
            return StepResult.Success();
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "not-streaming",
            EntryNodeId = "c",
            Nodes = [new NodeDefinition { Id = "c", StepType = "Check" }],
            Edges = []
        };

        await CreateRunner(registry).RunAsync(workflow);

        Assert.False(wasStreaming);
    }

    // ─── StreamMode filtering ───────────────────────────────────────

    [Fact]
    public async Task StreamAsync_MessagesMode_ExcludesTokenEvents()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Streamer", async ctx =>
        {
            if (ctx.OnToken != null)
            {
                await ctx.OnToken("tok1", ctx.CancellationToken);
                await ctx.OnToken("tok2", ctx.CancellationToken);
            }
            return StepResult.Success();
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "messages-mode",
            EntryNodeId = "s",
            Nodes = [new NodeDefinition { Id = "s", StepType = "Streamer" }],
            Edges = []
        };

        var runner = CreateRunner(registry);
        var events = new List<WorkflowEvent>();

        await foreach (var evt in runner.StreamAsync(workflow, mode: StreamMode.Messages))
        {
            events.Add(evt);
        }

        // Should have workflow/step events but NO token events
        Assert.DoesNotContain(events, e => e is TokenStreamEvent);
        Assert.Contains(events, e => e is WorkflowStartedEvent);
        Assert.Contains(events, e => e is WorkflowCompletedEvent);
        Assert.Contains(events, e => e is StepStartedEvent);
        Assert.Contains(events, e => e is StepCompletedEvent);
    }

    [Fact]
    public async Task StreamAsync_ValuesMode_OnlyStateChangedAndCompleted()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Writer", async ctx =>
        {
            if (ctx.OnToken != null)
                await ctx.OnToken("tok", ctx.CancellationToken);
            return StepResult.Success(new() { ["v"] = 1 });
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "values-mode",
            EntryNodeId = "w",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "w",
                    StepType = "Writer",
                    OutputMappings = new() { ["v"] = "Context.Value" }
                }
            ],
            Edges = []
        };

        var runner = CreateRunner(registry);
        var events = new List<WorkflowEvent>();

        await foreach (var evt in runner.StreamAsync(workflow, mode: StreamMode.Values))
        {
            events.Add(evt);
        }

        // Values mode: only StateChangedEvent and WorkflowCompletedEvent
        Assert.All(events, e =>
            Assert.True(e is StateChangedEvent or WorkflowCompletedEvent,
                $"Unexpected event type: {e.GetType().Name}"));
        Assert.Contains(events, e => e is StateChangedEvent);
        Assert.Contains(events, e => e is WorkflowCompletedEvent);
    }

    [Fact]
    public async Task StreamAsync_UpdatesMode_IncludesStepCompletedButNotStarted()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Ok", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "updates-mode",
            EntryNodeId = "ok",
            Nodes = [new NodeDefinition { Id = "ok", StepType = "Ok" }],
            Edges = []
        };

        var runner = CreateRunner(registry);
        var events = new List<WorkflowEvent>();

        await foreach (var evt in runner.StreamAsync(workflow, mode: StreamMode.Updates))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is StepCompletedEvent);
        Assert.Contains(events, e => e is WorkflowCompletedEvent);
        Assert.DoesNotContain(events, e => e is StepStartedEvent);
        Assert.DoesNotContain(events, e => e is WorkflowStartedEvent);
        Assert.DoesNotContain(events, e => e is TokenStreamEvent);
    }

    [Fact]
    public async Task StreamAsync_TokensMode_IncludesEverything()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Streamer", async ctx =>
        {
            if (ctx.OnToken != null)
                await ctx.OnToken("t", ctx.CancellationToken);
            return StepResult.Success(new() { ["x"] = 1 });
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "tokens-mode",
            EntryNodeId = "s",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "s",
                    StepType = "Streamer",
                    OutputMappings = new() { ["x"] = "Context.X" }
                }
            ],
            Edges = []
        };

        var runner = CreateRunner(registry);
        var events = new List<WorkflowEvent>();

        await foreach (var evt in runner.StreamAsync(workflow, mode: StreamMode.Tokens))
        {
            events.Add(evt);
        }

        // Tokens mode yields everything
        Assert.Contains(events, e => e is TokenStreamEvent);
        Assert.Contains(events, e => e is WorkflowStartedEvent);
        Assert.Contains(events, e => e is StepStartedEvent);
        Assert.Contains(events, e => e is StepCompletedEvent);
        Assert.Contains(events, e => e is StateChangedEvent);
        Assert.Contains(events, e => e is WorkflowCompletedEvent);
    }

    // ─── existing event sink still fires ────────────────────────────

    [Fact]
    public async Task StreamAsync_ExistingEventSink_StillReceivesEvents()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Ok", _ => StepResult.Success()));

        var externalSink = new RecordingEventSink();

        var workflow = new WorkflowDefinition
        {
            Id = "dual-sink",
            EntryNodeId = "ok",
            Nodes = [new NodeDefinition { Id = "ok", StepType = "Ok" }],
            Edges = []
        };

        var runner = CreateRunner(registry, eventSink: externalSink);

        await foreach (var _ in runner.StreamAsync(workflow)) { }

        // The external sink should have received the same events
        Assert.Contains(externalSink.Events, e => e is WorkflowStartedEvent);
        Assert.Contains(externalSink.Events, e => e is WorkflowCompletedEvent);
    }

    // ─── failure propagation ────────────────────────────────────────

    [Fact]
    public async Task StreamAsync_FailedStep_YieldsCompletedWithFailure()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Boom", _ =>
            StepResult.Fail("kaboom")));

        var workflow = new WorkflowDefinition
        {
            Id = "stream-fail",
            EntryNodeId = "boom",
            Nodes = [new NodeDefinition { Id = "boom", StepType = "Boom" }],
            Edges = []
        };

        var runner = CreateRunner(registry);
        var events = new List<WorkflowEvent>();

        await foreach (var evt in runner.StreamAsync(workflow))
        {
            events.Add(evt);
        }

        var completed = events.OfType<WorkflowCompletedEvent>().Single();
        Assert.False(completed.Success);
        Assert.NotEmpty(completed.Errors);
    }

    // ─── cancellation ───────────────────────────────────────────────

    [Fact]
    public async Task StreamAsync_Cancellation_StopsYielding()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Slow", async ctx =>
        {
            if (ctx.OnToken != null)
            {
                for (int i = 0; i < 100; i++)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    await ctx.OnToken($"token-{i}", ctx.CancellationToken);
                    await Task.Delay(50, ctx.CancellationToken);
                }
            }
            return StepResult.Success();
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "stream-cancel",
            EntryNodeId = "slow",
            Nodes = [new NodeDefinition { Id = "slow", StepType = "Slow" }],
            Edges = []
        };

        using var cts = new CancellationTokenSource();
        var runner = CreateRunner(registry);
        var events = new List<WorkflowEvent>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var evt in runner.StreamAsync(workflow, cancellationToken: cts.Token))
            {
                events.Add(evt);
                // Cancel after receiving a few token events
                if (events.OfType<TokenStreamEvent>().Count() >= 3)
                    cts.Cancel();
            }
        });

        // We should have received some events but not all 100 tokens
        var tokenCount = events.OfType<TokenStreamEvent>().Count();
        Assert.True(tokenCount >= 3, $"Expected at least 3 tokens, got {tokenCount}");
        Assert.True(tokenCount < 100, $"Expected fewer than 100 tokens, got {tokenCount}");
    }

    // ─── initial state passthrough ──────────────────────────────────

    [Fact]
    public async Task StreamAsync_InitialState_IsUsed()
    {
        string? receivedInput = null;

        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Reader", ctx =>
        {
            receivedInput = ctx.Inputs.GetValueOrDefault("val") as string;
            return StepResult.Success();
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "stream-init",
            EntryNodeId = "r",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "r",
                    StepType = "Reader",
                    InputMappings = new() { ["val"] = "Inputs.seed" }
                }
            ],
            Edges = []
        };

        var initialState = new WorkflowState
        {
            Inputs = new() { ["seed"] = "test-value" }
        };

        var runner = CreateRunner(registry);

        await foreach (var _ in runner.StreamAsync(workflow, initialState: initialState)) { }

        Assert.Equal("test-value", receivedInput);
    }

    // ─── token event metadata ───────────────────────────────────────

    [Fact]
    public async Task StreamAsync_TokenEvents_HaveCorrectMetadata()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("LLM", async ctx =>
        {
            if (ctx.OnToken != null)
                await ctx.OnToken("hello", ctx.CancellationToken);
            return StepResult.Success();
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "token-meta",
            EntryNodeId = "llm",
            Nodes = [new NodeDefinition { Id = "llm", StepType = "LLM" }],
            Edges = []
        };

        var runner = CreateRunner(registry);
        TokenStreamEvent? captured = null;

        await foreach (var evt in runner.StreamAsync(workflow))
        {
            if (evt is TokenStreamEvent te)
                captured = te;
        }

        Assert.NotNull(captured);
        Assert.Equal("token-meta", captured.WorkflowId);
        Assert.Equal("llm", captured.NodeId);
        Assert.Equal(nameof(TokenStreamEvent), captured.EventType);
        Assert.NotEqual(Guid.Empty, captured.EventId);
        Assert.True(captured.Timestamp <= DateTimeOffset.UtcNow);
    }

    // ─── multi-step pipeline with tokens ────────────────────────────

    [Fact]
    public async Task StreamAsync_ThreeStepPipeline_EventsInCorrectOrder()
    {
        var registry = new InMemoryStepRegistry();
        foreach (var name in new[] { "A", "B", "C" })
        {
            var captured = name;
            registry.Register(new LambdaStep(captured, async ctx =>
            {
                if (ctx.OnToken != null)
                    await ctx.OnToken($"{captured}-token", ctx.CancellationToken);
                return StepResult.Success(new() { ["step"] = captured });
            }));
        }

        var workflow = new WorkflowDefinition
        {
            Id = "stream-pipeline",
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

        var runner = CreateRunner(registry);
        var events = new List<WorkflowEvent>();

        await foreach (var evt in runner.StreamAsync(workflow))
        {
            events.Add(evt);
        }

        // Verify ordering: WorkflowStarted comes first, WorkflowCompleted last
        Assert.IsType<WorkflowStartedEvent>(events.First());
        Assert.IsType<WorkflowCompletedEvent>(events.Last());

        // Each node should produce: StepStarted → TokenStream → StepCompleted
        var n1Events = events.Where(e => e.NodeId == "n1").ToList();
        Assert.Contains(n1Events, e => e is StepStartedEvent);
        Assert.Contains(n1Events, e => e is TokenStreamEvent);
        Assert.Contains(n1Events, e => e is StepCompletedEvent);

        // Tokens for each node should appear between their step started/completed
        var n1StartIdx = events.FindIndex(e => e is StepStartedEvent { NodeId: "n1" });
        var n1TokenIdx = events.FindIndex(e => e is TokenStreamEvent { NodeId: "n1" });
        var n1EndIdx = events.FindIndex(e => e is StepCompletedEvent { NodeId: "n1" });
        Assert.True(n1StartIdx < n1TokenIdx);
        Assert.True(n1TokenIdx < n1EndIdx);

        // All 3 token events present
        var allTokens = events.OfType<TokenStreamEvent>().ToList();
        Assert.Equal(3, allTokens.Count);
        Assert.Equal("A-token", allTokens[0].Token);
        Assert.Equal("B-token", allTokens[1].Token);
        Assert.Equal("C-token", allTokens[2].Token);
    }
}