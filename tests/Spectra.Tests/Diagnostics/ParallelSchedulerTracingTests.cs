using System.Collections.Concurrent;
using System.Diagnostics;
using Spectra.Contracts.Diagnostics;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Diagnostics;
using Spectra.Kernel.Scheduling;
using Xunit;

namespace Spectra.Tests.Diagnostics;

public class ParallelSchedulerTracingTests : IDisposable
{
    // ─── tracing infrastructure ──────────────────────────────────────

    private readonly ConcurrentBag<Activity> _collectedActivities = [];
    private readonly ConcurrentBag<Activity> _stoppedActivities = [];
    private readonly ActivityListener _listener;

    public ParallelSchedulerTracingTests()
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

    private static IStepRegistry CreateRegistry(params IStep[] steps)
    {
        var registry = new TestRegistry();
        foreach (var step in steps)
            registry.Register(step);
        return registry;
    }

    private sealed class TestRegistry : IStepRegistry
    {
        private readonly Dictionary<string, IStep> _steps = new(StringComparer.OrdinalIgnoreCase);
        public IStep? GetStep(string stepType) => _steps.GetValueOrDefault(stepType);
        public void Register(IStep step) => _steps[step.StepType] = step;
    }

    private sealed class SimpleStep : IStep
    {
        public string StepType { get; }
        private readonly Func<StepContext, Task<StepResult>> _execute;

        public SimpleStep(string stepType, Func<StepContext, StepResult> execute)
        {
            StepType = stepType;
            _execute = ctx => Task.FromResult(execute(ctx));
        }

        public SimpleStep(string stepType, Func<StepContext, Task<StepResult>> execute)
        {
            StepType = stepType;
            _execute = execute;
        }

        public Task<StepResult> ExecuteAsync(StepContext context) => _execute(context);
    }

    // ─── workflow.run span ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CreatesWorkflowRunSpan()
    {
        var registry = CreateRegistry(
            new SimpleStep("Marker", _ => StepResult.Success(new() { ["done"] = true })));

        var workflow = new WorkflowDefinition
        {
            Id = "par-trace-wf",
            Name = "Parallel Traced",
            EntryNodeId = "s1",
            Nodes = [new NodeDefinition { Id = "s1", StepType = "Marker" }],
            Edges = []
        };

        var scheduler = new ParallelScheduler(registry, maxConcurrency: 4);
        var state = await scheduler.ExecuteAsync(workflow);

        var workflowSpan = _collectedActivities
            .FirstOrDefault(a => a.OperationName == "workflow.run"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "par-trace-wf");

        Assert.NotNull(workflowSpan);
        Assert.Equal("par-trace-wf", workflowSpan.GetTagItem(SpectraTags.WorkflowId));
        Assert.Equal(state.RunId, workflowSpan.GetTagItem(SpectraTags.RunId));
        Assert.Equal("Parallel Traced", workflowSpan.GetTagItem(SpectraTags.WorkflowName));
    }

    [Fact]
    public async Task ExecuteAsync_WorkflowSpan_OkOnSuccess()
    {
        var registry = CreateRegistry(
            new SimpleStep("Ok", _ => StepResult.Success()));

        var workflow = new WorkflowDefinition
        {
            Id = "par-ok",
            EntryNodeId = "n",
            Nodes = [new NodeDefinition { Id = "n", StepType = "Ok" }],
            Edges = []
        };

        var scheduler = new ParallelScheduler(registry, maxConcurrency: 4);
        await scheduler.ExecuteAsync(workflow);

        var workflowSpan = _stoppedActivities
            .First(a => a.OperationName == "workflow.run"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "par-ok");

        Assert.Equal(ActivityStatusCode.Ok, workflowSpan.Status);
    }

    [Fact]
    public async Task ExecuteAsync_WorkflowSpan_ErrorOnFailure()
    {
        var registry = CreateRegistry(
            new SimpleStep("Fail", _ => StepResult.Fail("kaboom")));

        var workflow = new WorkflowDefinition
        {
            Id = "par-fail",
            EntryNodeId = "n",
            Nodes = [new NodeDefinition { Id = "n", StepType = "Fail" }],
            Edges = []
        };

        var scheduler = new ParallelScheduler(registry, maxConcurrency: 4);
        await scheduler.ExecuteAsync(workflow);

        var workflowSpan = _stoppedActivities
            .First(a => a.OperationName == "workflow.run"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "par-fail");

        Assert.Equal(ActivityStatusCode.Error, workflowSpan.Status);
    }

    [Fact]
    public async Task ExecuteAsync_WorkflowSpan_RecordsStepsExecuted()
    {
        var registry = CreateRegistry(
            new SimpleStep("M", _ => StepResult.Success(new() { ["ok"] = true })));

        var workflow = new WorkflowDefinition
        {
            Id = "par-count",
            EntryNodeId = "start",
            Nodes =
            [
                new NodeDefinition { Id = "start", StepType = "M" },
                new NodeDefinition { Id = "a", StepType = "M" },
                new NodeDefinition { Id = "b", StepType = "M" }
            ],
            Edges =
            [
                new EdgeDefinition { From = "start", To = "a" },
                new EdgeDefinition { From = "start", To = "b" }
            ]
        };

        var scheduler = new ParallelScheduler(registry, maxConcurrency: 4);
        await scheduler.ExecuteAsync(workflow);

        var workflowSpan = _stoppedActivities
            .First(a => a.OperationName == "workflow.run"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "par-count");

        // start succeeds, a and b succeed in parallel → 3 steps
        Assert.Equal(3, workflowSpan.GetTagItem(SpectraTags.StepsExecuted));
    }

    // ─── parallel batch spans ────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CreatesBatchSpanForParallelNodes()
    {
        var registry = CreateRegistry(
            new SimpleStep("Fast", _ => StepResult.Success(new() { ["ok"] = true })));

        var workflow = new WorkflowDefinition
        {
            Id = "batch-trace",
            EntryNodeId = "start",
            Nodes =
            [
                new NodeDefinition { Id = "start", StepType = "Fast" },
                new NodeDefinition { Id = "p1", StepType = "Fast" },
                new NodeDefinition { Id = "p2", StepType = "Fast" }
            ],
            Edges =
            [
                new EdgeDefinition { From = "start", To = "p1" },
                new EdgeDefinition { From = "start", To = "p2" }
            ]
        };

        var scheduler = new ParallelScheduler(registry, maxConcurrency: 4);
        await scheduler.ExecuteAsync(workflow);

        var batchSpans = _collectedActivities
            .Where(a => a.OperationName == "workflow.parallel_batch"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "batch-trace")
            .ToList();

        // At least one batch span (the one with p1+p2)
        Assert.NotEmpty(batchSpans);

        var parallelBatch = batchSpans
            .FirstOrDefault(b => (int?)b.GetTagItem(SpectraTags.BatchSize) >= 2);

        Assert.NotNull(parallelBatch);
    }

    [Fact]
    public async Task ExecuteAsync_BatchSpan_RecordsSuccessAndFailureCounts()
    {
        var registry = CreateRegistry(
            new SimpleStep("Ok", _ => StepResult.Success(new() { ["ok"] = true })),
            new SimpleStep("Bad", _ => StepResult.Fail("oops")));

        var workflow = new WorkflowDefinition
        {
            Id = "batch-counts",
            EntryNodeId = "start",
            Nodes =
            [
                new NodeDefinition { Id = "start", StepType = "Ok" },
                new NodeDefinition { Id = "good", StepType = "Ok" },
                new NodeDefinition { Id = "bad", StepType = "Bad" }
            ],
            Edges =
            [
                new EdgeDefinition { From = "start", To = "good" },
                new EdgeDefinition { From = "start", To = "bad" }
            ]
        };

        var scheduler = new ParallelScheduler(registry, maxConcurrency: 4);
        await scheduler.ExecuteAsync(workflow);

        var batchSpan = _collectedActivities
            .Where(a => a.OperationName == "workflow.parallel_batch"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "batch-counts")
            .FirstOrDefault(b => (int?)b.GetTagItem(SpectraTags.BatchSize) >= 2);

        Assert.NotNull(batchSpan);
        Assert.Equal(1, batchSpan.GetTagItem(SpectraTags.BatchSuccessCount));
        Assert.Equal(1, batchSpan.GetTagItem(SpectraTags.BatchFailureCount));
    }

    // ─── step.execute spans ──────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CreatesStepSpanPerNode()
    {
        var registry = CreateRegistry(
            new SimpleStep("W", _ => StepResult.Success(new() { ["ok"] = true })));

        var workflow = new WorkflowDefinition
        {
            Id = "par-steps",
            EntryNodeId = "start",
            Nodes =
            [
                new NodeDefinition { Id = "start", StepType = "W" },
                new NodeDefinition { Id = "a", StepType = "W" },
                new NodeDefinition { Id = "b", StepType = "W" }
            ],
            Edges =
            [
                new EdgeDefinition { From = "start", To = "a" },
                new EdgeDefinition { From = "start", To = "b" }
            ]
        };

        var scheduler = new ParallelScheduler(registry, maxConcurrency: 4);
        await scheduler.ExecuteAsync(workflow);

        var stepSpans = _collectedActivities
            .Where(a => a.OperationName == "step.execute"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "par-steps")
            .ToList();

        Assert.Equal(3, stepSpans.Count);

        var nodeIds = stepSpans
            .Select(s => (string)s.GetTagItem(SpectraTags.NodeId)!)
            .ToHashSet();

        Assert.Contains("start", nodeIds);
        Assert.Contains("a", nodeIds);
        Assert.Contains("b", nodeIds);
    }

    [Fact]
    public async Task ExecuteAsync_StepSpan_RecordsFailureStatus()
    {
        var registry = CreateRegistry(
            new SimpleStep("Boom", _ => StepResult.Fail("exploded")));

        var workflow = new WorkflowDefinition
        {
            Id = "par-step-fail",
            EntryNodeId = "n",
            Nodes = [new NodeDefinition { Id = "n", StepType = "Boom" }],
            Edges = []
        };

        var scheduler = new ParallelScheduler(registry, maxConcurrency: 4);
        await scheduler.ExecuteAsync(workflow);

        var stepSpan = _stoppedActivities
            .First(a => a.OperationName == "step.execute"
                && (string?)a.GetTagItem(SpectraTags.WorkflowId) == "par-step-fail");

        Assert.Equal("Failed", stepSpan.GetTagItem(SpectraTags.StepStatus));
        Assert.Equal(ActivityStatusCode.Error, stepSpan.Status);
    }

    // ─── all spans share RunId ───────────────────────────────────────

    [Fact]
    public async Task AllSpans_ShareSameRunId()
    {
        var registry = CreateRegistry(
            new SimpleStep("S", _ => StepResult.Success(new() { ["ok"] = true })));

        var workflow = new WorkflowDefinition
        {
            Id = "par-runid",
            EntryNodeId = "start",
            Nodes =
            [
                new NodeDefinition { Id = "start", StepType = "S" },
                new NodeDefinition { Id = "child", StepType = "S" }
            ],
            Edges = [new EdgeDefinition { From = "start", To = "child" }]
        };

        var scheduler = new ParallelScheduler(registry, maxConcurrency: 4);
        var state = await scheduler.ExecuteAsync(workflow);

        var relevantSpans = _collectedActivities
            .Where(a => (string?)a.GetTagItem(SpectraTags.WorkflowId) == "par-runid")
            .ToList();

        Assert.True(relevantSpans.Count >= 2);

        foreach (var span in relevantSpans)
        {
            Assert.Equal(state.RunId, span.GetTagItem(SpectraTags.RunId));
        }
    }
}