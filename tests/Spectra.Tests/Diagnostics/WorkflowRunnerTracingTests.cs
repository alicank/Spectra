using System.Collections.Concurrent;
using System.Diagnostics;
using Spectra.Contracts.Diagnostics;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Diagnostics;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Diagnostics;

public class WorkflowRunnerTracingTests : IDisposable
{
    // ─── tracing infrastructure ──────────────────────────────────────

    private readonly ConcurrentBag<Activity> _collectedActivities = [];
    private readonly ConcurrentBag<Activity> _stoppedActivities = [];
    private readonly ActivityListener _listener;

    public WorkflowRunnerTracingTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SpectraActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _collectedActivities.Add(activity),
            ActivityStopped = activity => _stoppedActivities.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        GC.SuppressFinalize(this);
    }

    // ─── helpers ────────────────────────────────────────────────────

    private static WorkflowRunner CreateRunner(
        IStepRegistry registry,
        IStateMapper? stateMapper = null,
        IEventSink? eventSink = null,
        IInterruptHandler? interruptHandler = null)
    {
        return new WorkflowRunner(
            registry,
            stateMapper ?? new StateMapper(),
            eventSink: eventSink,
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
        public IStep? GetStep(string stepType) => _steps.GetValueOrDefault(stepType);
        public void Register(IStep step) => _steps[step.StepType] = step;
    }

    private sealed class StateMapper : IStateMapper
    {
        public Dictionary<string, object?> ResolveInputs(NodeDefinition node, WorkflowState state)
        {
            var inputs = new Dictionary<string, object?>();
            foreach (var mapping in node.InputMappings)
            {
                var parts = mapping.Value.Split('.', 2);
                if (parts.Length == 2)
                {
                    var section = parts[0];
                    var key = parts[1];
                    var dict = section switch
                    {
                        "Inputs" => state.Inputs,
                        "Context" => state.Context,
                        "Artifacts" => state.Artifacts,
                        _ => null
                    };
                    if (dict != null && dict.TryGetValue(key, out var val))
                        inputs[mapping.Key] = val;
                }
            }
            foreach (var param in node.Parameters)
            {
                if (!inputs.ContainsKey(param.Key))
                    inputs[param.Key] = param.Value;
            }
            return inputs;
        }

        public void ApplyOutputs(NodeDefinition node, WorkflowState state, Dictionary<string, object?> outputs)
        {
            foreach (var mapping in node.OutputMappings)
            {
                if (outputs.TryGetValue(mapping.Key, out var value))
                {
                    var parts = mapping.Value.Split('.', 2);
                    var section = parts[0];
                    var key = parts.Length > 1 ? parts[1] : mapping.Key;
                    var dict = section switch
                    {
                        "Context" => state.Context,
                        "Artifacts" => state.Artifacts,
                        _ => state.Context
                    };
                    dict[key] = value;
                }
            }
            if (node.OutputMappings.Count == 0 && outputs.Count > 0)
                state.Context[node.Id] = outputs;
        }
    }

    // ─── workflow.run span ───────────────────────────────────────────

    [Fact]
    public async Task RunAsync_CreatesWorkflowRunSpan()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Noop", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "trace-wf",
            Name = "Traced Workflow",
            EntryNodeId = "n1",
            Nodes = [new NodeDefinition { Id = "n1", StepType = "Noop" }],
            Edges = []
        };

        var runner = CreateRunner(registry);
        var state = await runner.RunAsync(workflow);

        var workflowSpan = _collectedActivities
            .FirstOrDefault(a => a.OperationName == "workflow.run"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "trace-wf");

        Assert.NotNull(workflowSpan);
        Assert.Equal("trace-wf", workflowSpan.GetTagItem(SpectraTags.WorkflowId));
        Assert.Equal(state.RunId, workflowSpan.GetTagItem(SpectraTags.RunId));
        Assert.Equal("Traced Workflow", workflowSpan.GetTagItem(SpectraTags.WorkflowName));
    }

    [Fact]
    public async Task RunAsync_WorkflowSpan_HasOkStatusOnSuccess()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Ok", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "ok-wf",
            EntryNodeId = "n",
            Nodes = [new NodeDefinition { Id = "n", StepType = "Ok" }],
            Edges = []
        };

        await CreateRunner(registry).RunAsync(workflow);

        var workflowSpan = _stoppedActivities
            .First(a => a.OperationName == "workflow.run"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "ok-wf");

        Assert.Equal(ActivityStatusCode.Ok, workflowSpan.Status);
    }

    [Fact]
    public async Task RunAsync_WorkflowSpan_HasErrorStatusOnFailure()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Fail", _ => StepResult.Fail("boom")));

        var workflow = new WorkflowDefinition
        {
            Id = "fail-wf",
            EntryNodeId = "n",
            Nodes = [new NodeDefinition { Id = "n", StepType = "Fail" }],
            Edges = []
        };

        await CreateRunner(registry).RunAsync(workflow);

        var workflowSpan = _stoppedActivities
            .First(a => a.OperationName == "workflow.run"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "fail-wf");

        Assert.Equal(ActivityStatusCode.Error, workflowSpan.Status);
    }

    [Fact]
    public async Task RunAsync_WorkflowSpan_RecordsStepsExecutedTag()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("A", _ => StepResult.Success()));
        registry.Register(new LambdaStep("B", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "count-wf",
            EntryNodeId = "n1",
            Nodes =
            [
                new NodeDefinition { Id = "n1", StepType = "A" },
                new NodeDefinition { Id = "n2", StepType = "B" }
            ],
            Edges = [new EdgeDefinition { From = "n1", To = "n2" }]
        };

        await CreateRunner(registry).RunAsync(workflow);

        var workflowSpan = _stoppedActivities
            .First(a => a.OperationName == "workflow.run"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "count-wf");

        Assert.Equal(2, workflowSpan.GetTagItem(SpectraTags.StepsExecuted));
    }

    // ─── step.execute spans ──────────────────────────────────────────

    [Fact]
    public async Task RunAsync_CreatesStepExecuteSpanPerNode()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("S1", _ => StepResult.Success()));
        registry.Register(new LambdaStep("S2", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "step-spans",
            EntryNodeId = "a",
            Nodes =
            [
                new NodeDefinition { Id = "a", StepType = "S1" },
                new NodeDefinition { Id = "b", StepType = "S2" }
            ],
            Edges = [new EdgeDefinition { From = "a", To = "b" }]
        };

        await CreateRunner(registry).RunAsync(workflow);

        var stepSpans = _collectedActivities
            .Where(a => a.OperationName == "step.execute"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "step-spans")
            .ToList();

        Assert.Equal(2, stepSpans.Count);

        var spanA = stepSpans.First(s => (string)s.GetTagItem(SpectraTags.NodeId)! == "a");
        Assert.Equal("S1", spanA.GetTagItem(SpectraTags.StepType));

        var spanB = stepSpans.First(s => (string)s.GetTagItem(SpectraTags.NodeId)! == "b");
        Assert.Equal("S2", spanB.GetTagItem(SpectraTags.StepType));
    }

    [Fact]
    public async Task RunAsync_StepSpan_HasCorrectStatusOnSuccess()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Ok", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "step-ok",
            EntryNodeId = "n",
            Nodes = [new NodeDefinition { Id = "n", StepType = "Ok" }],
            Edges = []
        };

        await CreateRunner(registry).RunAsync(workflow);

        var stepSpan = _stoppedActivities
            .First(a => a.OperationName == "step.execute"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "step-ok");

        Assert.Equal("Succeeded", stepSpan.GetTagItem(SpectraTags.StepStatus));
    }

    [Fact]
    public async Task RunAsync_StepSpan_RecordsErrorOnFailure()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Bad", _ => StepResult.Fail("step broke")));

        var workflow = new WorkflowDefinition
        {
            Id = "step-fail",
            EntryNodeId = "n",
            Nodes = [new NodeDefinition { Id = "n", StepType = "Bad" }],
            Edges = []
        };

        await CreateRunner(registry).RunAsync(workflow);

        var stepSpan = _stoppedActivities
            .First(a => a.OperationName == "step.execute"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "step-fail");

        Assert.Equal("Failed", stepSpan.GetTagItem(SpectraTags.StepStatus));
        Assert.Equal(ActivityStatusCode.Error, stepSpan.Status);
        Assert.Equal("step broke", stepSpan.GetTagItem(SpectraTags.ErrorMessage));
    }

    // ─── interrupt tracing ───────────────────────────────────────────

    [Fact]
    public async Task RunAsync_StepSpan_RecordsInterruptOnException()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Gated", async ctx =>
        {
            await ctx.InterruptAsync(new InterruptRequest
            {
                RunId = ctx.RunId,
                WorkflowId = ctx.WorkflowId,
                NodeId = ctx.NodeId,
                Reason = "needs-approval"
            });
            return StepResult.Success();
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "interrupt-trace",
            EntryNodeId = "g",
            Nodes = [new NodeDefinition { Id = "g", StepType = "Gated" }],
            Edges = []
        };

        // No handler → InterruptException → suspend
        await CreateRunner(registry).RunAsync(workflow);

        var stepSpan = _stoppedActivities
            .First(a => a.OperationName == "step.execute"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "interrupt-trace");

        Assert.Equal("interrupted", stepSpan.GetTagItem(SpectraTags.StepStatus));
        Assert.Equal("needs-approval", stepSpan.GetTagItem(SpectraTags.InterruptReason));
    }

    // ─── StepContext.TracingActivity ──────────────────────────────────

    [Fact]
    public async Task StepContext_TracingActivity_ExposesCurrentActivity()
    {
        Activity? capturedActivity = null;

        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Capture", ctx =>
        {
            capturedActivity = ctx.TracingActivity;
            // Step can enrich the span
            capturedActivity?.SetTag("custom.tag", "custom-value");
            return StepResult.Success();
        }));

        var workflow = new WorkflowDefinition
        {
            Id = "ctx-activity",
            EntryNodeId = "c",
            Nodes = [new NodeDefinition { Id = "c", StepType = "Capture" }],
            Edges = []
        };

        await CreateRunner(registry).RunAsync(workflow);

        Assert.NotNull(capturedActivity);
        Assert.Equal("step.execute", capturedActivity.OperationName);
        Assert.Equal("custom-value", capturedActivity.GetTagItem("custom.tag"));
    }

    // ─── no listener = no spans ──────────────────────────────────────

    [Fact]
    public async Task NoListener_ProducesNoActivities()
    {
        // Dispose the listener so nothing is collected
        _listener.Dispose();

        var isolatedActivities = new List<Activity>();

        // Don't add a new listener — run with none
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("Noop", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "no-listener",
            EntryNodeId = "n",
            Nodes = [new NodeDefinition { Id = "n", StepType = "Noop" }],
            Edges = []
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        // Workflow still succeeds — tracing is purely additive
        Assert.Empty(state.Errors);

        // No spans should have been collected in our original list after disposal
        var postDisposalSpans = _collectedActivities
            .Where(a => (string?)a.GetTagItem(SpectraTags.WorkflowId) == "no-listener")
            .ToList();
        Assert.Empty(postDisposalSpans);
    }

    // ─── multi-step pipeline tracing ─────────────────────────────────

    [Fact]
    public async Task ThreeStepPipeline_AllStepsGetSpans()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("A", _ => StepResult.Success()));
        registry.Register(new LambdaStep("B", _ => StepResult.Success()));
        registry.Register(new LambdaStep("C", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "pipeline-trace",
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

        await CreateRunner(registry).RunAsync(workflow);

        var stepSpans = _collectedActivities
            .Where(a => a.OperationName == "step.execute"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "pipeline-trace")
            .ToList();

        Assert.Equal(3, stepSpans.Count);

        var nodeIds = stepSpans
            .Select(s => (string)s.GetTagItem(SpectraTags.NodeId)!)
            .ToList();

        Assert.Contains("n1", nodeIds);
        Assert.Contains("n2", nodeIds);
        Assert.Contains("n3", nodeIds);
    }

    // ─── all spans share the same RunId ──────────────────────────────

    [Fact]
    public async Task AllSpans_ShareSameRunId()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new LambdaStep("X", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "runid-trace",
            EntryNodeId = "n",
            Nodes = [new NodeDefinition { Id = "n", StepType = "X" }],
            Edges = []
        };

        var state = await CreateRunner(registry).RunAsync(workflow);

        var relevantSpans = _collectedActivities
            .Where(a => (string?)a.GetTagItem(SpectraTags.WorkflowId) == "runid-trace")
            .ToList();

        Assert.True(relevantSpans.Count >= 2); // at least workflow + step

        foreach (var span in relevantSpans)
        {
            Assert.Equal(state.RunId, span.GetTagItem(SpectraTags.RunId));
        }
    }
}