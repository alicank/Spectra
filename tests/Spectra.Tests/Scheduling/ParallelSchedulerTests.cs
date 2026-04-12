using System.Diagnostics;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Scheduling;
using Xunit;

namespace Spectra.Tests.Scheduling;

public class ParallelSchedulerTests
{
    private static IStepRegistry CreateRegistry(params IStep[] steps)
    {
        var registry = new TestStepRegistry();
        foreach (var step in steps)
            registry.Register(step);
        return registry;
    }

    [Fact]
    public async Task ExecutesFanOutInParallel()
    {
        // Start -> (A, B, C in parallel)
        var registry = CreateRegistry(new DelayStep(), new MarkerStep());

        var workflow = new WorkflowDefinition
        {
            Id = "fan-out-test",
            EntryNodeId = "start",
            Nodes =
            [
                new NodeDefinition { Id = "start", StepType = "Marker", Parameters = new() { ["name"] = "Start" } },
                new NodeDefinition { Id = "taskA", StepType = "Delay", Parameters = new() { ["milliseconds"] = 200, ["label"] = "Task A" } },
                new NodeDefinition { Id = "taskB", StepType = "Delay", Parameters = new() { ["milliseconds"] = 200, ["label"] = "Task B" } },
                new NodeDefinition { Id = "taskC", StepType = "Delay", Parameters = new() { ["milliseconds"] = 200, ["label"] = "Task C" } }
            ],
            Edges =
            [
                new EdgeDefinition { From = "start", To = "taskA" },
                new EdgeDefinition { From = "start", To = "taskB" },
                new EdgeDefinition { From = "start", To = "taskC" }
            ]
        };

        var scheduler = new ParallelScheduler(registry, maxConcurrency: 4);

        var stopwatch = Stopwatch.StartNew();
        var state = await scheduler.ExecuteAsync(workflow);
        stopwatch.Stop();

        Assert.Empty(state.Errors);
        Assert.True(state.Context.ContainsKey("taskA"));
        Assert.True(state.Context.ContainsKey("taskB"));
        Assert.True(state.Context.ContainsKey("taskC"));

        // If truly parallel, should take ~200ms, not ~600ms
        Assert.True(stopwatch.ElapsedMilliseconds < 500,
            $"Should run in parallel, took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ExecutesFanInJoin()
    {
        // Start -> (A, B, C in parallel) -> Join (waits for all)
        var registry = CreateRegistry(new DelayStep(), new MarkerStep(), new AggregateStep());

        var workflow = new WorkflowDefinition
        {
            Id = "fan-in-test",
            EntryNodeId = "start",
            Nodes =
            [
                new NodeDefinition { Id = "start", StepType = "Marker", Parameters = new() { ["name"] = "Start" } },
                new NodeDefinition { Id = "taskA", StepType = "Delay", Parameters = new() { ["milliseconds"] = 100, ["label"] = "A" } },
                new NodeDefinition { Id = "taskB", StepType = "Delay", Parameters = new() { ["milliseconds"] = 150, ["label"] = "B" } },
                new NodeDefinition { Id = "taskC", StepType = "Delay", Parameters = new() { ["milliseconds"] = 200, ["label"] = "C" } },
                new NodeDefinition { Id = "join", StepType = "Aggregate", WaitForAll = true }
            ],
            Edges =
            [
                new EdgeDefinition { From = "start", To = "taskA" },
                new EdgeDefinition { From = "start", To = "taskB" },
                new EdgeDefinition { From = "start", To = "taskC" },
                new EdgeDefinition { From = "taskA", To = "join" },
                new EdgeDefinition { From = "taskB", To = "join" },
                new EdgeDefinition { From = "taskC", To = "join" }
            ]
        };

        var scheduler = new ParallelScheduler(registry, maxConcurrency: 4);

        var state = await scheduler.ExecuteAsync(workflow);

        Assert.Empty(state.Errors);
        Assert.True(state.Context.ContainsKey("join"), "Join should have executed");

        var joinOutput = state.Context["join"] as Dictionary<string, object?>;
        Assert.NotNull(joinOutput);
        Assert.True((bool)joinOutput["aggregated"]!);
    }

    [Fact]
    public async Task RespectsMaxConcurrency()
    {
        var registry = CreateRegistry(new TrackingDelayStep());

        // 4 parallel tasks but max concurrency of 2
        var workflow = new WorkflowDefinition
        {
            Id = "concurrency-test",
            EntryNodeId = "start",
            Nodes =
            [
                new NodeDefinition { Id = "start", StepType = "TrackingDelay", Parameters = new() { ["milliseconds"] = 10 } },
                new NodeDefinition { Id = "t1", StepType = "TrackingDelay", Parameters = new() { ["milliseconds"] = 100 } },
                new NodeDefinition { Id = "t2", StepType = "TrackingDelay", Parameters = new() { ["milliseconds"] = 100 } },
                new NodeDefinition { Id = "t3", StepType = "TrackingDelay", Parameters = new() { ["milliseconds"] = 100 } },
                new NodeDefinition { Id = "t4", StepType = "TrackingDelay", Parameters = new() { ["milliseconds"] = 100 } }
            ],
            Edges =
            [
                new EdgeDefinition { From = "start", To = "t1" },
                new EdgeDefinition { From = "start", To = "t2" },
                new EdgeDefinition { From = "start", To = "t3" },
                new EdgeDefinition { From = "start", To = "t4" }
            ]
        };

        TrackingDelayStep.Reset();
        var scheduler = new ParallelScheduler(registry, maxConcurrency: 2);

        var state = await scheduler.ExecuteAsync(workflow);

        Assert.Empty(state.Errors);
        Assert.True(TrackingDelayStep.MaxConcurrent <= 2,
            $"Max concurrency exceeded: {TrackingDelayStep.MaxConcurrent}");
    }

    [Fact]
    public async Task ExecutesDiamondPattern()
    {
        // Diamond: Start -> (A, B parallel) -> End (joins)
        var registry = CreateRegistry(new DelayStep(), new MarkerStep());

        var workflow = new WorkflowDefinition
        {
            Id = "diamond-parallel",
            EntryNodeId = "start",
            Nodes =
            [
                new NodeDefinition { Id = "start", StepType = "Marker", Parameters = new() { ["name"] = "Start" } },
                new NodeDefinition { Id = "pathA", StepType = "Delay", Parameters = new() { ["milliseconds"] = 150, ["label"] = "Path A" } },
                new NodeDefinition { Id = "pathB", StepType = "Delay", Parameters = new() { ["milliseconds"] = 100, ["label"] = "Path B" } },
                new NodeDefinition { Id = "end", StepType = "Marker", Parameters = new() { ["name"] = "End" }, WaitForAll = true }
            ],
            Edges =
            [
                new EdgeDefinition { From = "start", To = "pathA" },
                new EdgeDefinition { From = "start", To = "pathB" },
                new EdgeDefinition { From = "pathA", To = "end" },
                new EdgeDefinition { From = "pathB", To = "end" }
            ]
        };

        var scheduler = new ParallelScheduler(registry, maxConcurrency: 4);

        var stopwatch = Stopwatch.StartNew();
        var state = await scheduler.ExecuteAsync(workflow);
        stopwatch.Stop();

        Assert.Empty(state.Errors);
        Assert.True(state.Context.ContainsKey("start"));
        Assert.True(state.Context.ContainsKey("pathA"));
        Assert.True(state.Context.ContainsKey("pathB"));
        Assert.True(state.Context.ContainsKey("end"));

        // Should take ~150ms (slowest parallel path), not 250ms sequential
        Assert.True(stopwatch.ElapsedMilliseconds < 400,
            $"Diamond should run parallel paths concurrently, took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task HandlesPartialFailureInParallel()
    {
        var registry = CreateRegistry(new DelayStep(), new FailingStep(), new MarkerStep());

        var workflow = new WorkflowDefinition
        {
            Id = "partial-failure",
            EntryNodeId = "start",
            Nodes =
            [
                new NodeDefinition { Id = "start", StepType = "Marker", Parameters = new() { ["name"] = "Start" } },
                new NodeDefinition { Id = "taskOk", StepType = "Delay", Parameters = new() { ["milliseconds"] = 50 } },
                new NodeDefinition { Id = "taskFail", StepType = "Failing", Parameters = new() { ["message"] = "Intentional failure" } }
            ],
            Edges =
            [
                new EdgeDefinition { From = "start", To = "taskOk" },
                new EdgeDefinition { From = "start", To = "taskFail" }
            ]
        };

        var scheduler = new ParallelScheduler(registry, maxConcurrency: 4);

        var state = await scheduler.ExecuteAsync(workflow);

        Assert.NotEmpty(state.Errors);
        Assert.Contains("Intentional failure", state.Errors[0]);
    }

    [Fact]
    public async Task EmitsEventsWhenSinkProvided()
    {
        var registry = CreateRegistry(new MarkerStep());
        var eventSink = new CollectingEventSink();

        var workflow = new WorkflowDefinition
        {
            Id = "events-test",
            EntryNodeId = "step1",
            Nodes =
            [
                new NodeDefinition { Id = "step1", StepType = "Marker", Parameters = new() { ["name"] = "Only" } }
            ],
            Edges = []
        };

        var scheduler = new ParallelScheduler(registry, eventSink, maxConcurrency: 4);

        var state = await scheduler.ExecuteAsync(workflow);

        Assert.Empty(state.Errors);
        Assert.Contains(eventSink.Events, e => e is WorkflowStartedEvent);
        Assert.Contains(eventSink.Events, e => e is StepStartedEvent);
        Assert.Contains(eventSink.Events, e => e is StepCompletedEvent);
        Assert.Contains(eventSink.Events, e => e is WorkflowCompletedEvent);
    }

    [Fact]
    public async Task LoopbackEdge_ReentersCompletedNode()
    {
        var counter = new CountingStep();
        var registry = CreateRegistry(new MarkerStep(), counter);

        var workflow = new WorkflowDefinition
        {
            Id = "parallel-loopback",
            EntryNodeId = "start",
            MaxNodeIterations = 3,
            Nodes =
            [
                new NodeDefinition { Id = "start", StepType = "Marker", Parameters = new() { ["name"] = "Start" } },
                new NodeDefinition { Id = "worker", StepType = "Counting" }
            ],
            Edges =
            [
                new EdgeDefinition { From = "start", To = "worker" },
                new EdgeDefinition { From = "worker", To = "worker", IsLoopback = true }
            ]
        };

        var scheduler = new ParallelScheduler(registry, maxConcurrency: 4);
        var state = await scheduler.ExecuteAsync(workflow);

        // Worker should execute multiple times (initial + up to MaxNodeIterations)
        Assert.True(counter.ExecutionCount >= 2, $"Expected multiple executions, got {counter.ExecutionCount}");
    }
}

#region Test Infrastructure

internal class TestStepRegistry : IStepRegistry
{
    private readonly Dictionary<string, IStep> _steps = [];

    public IStep? GetStep(string stepType) =>
        _steps.GetValueOrDefault(stepType);

    public void Register(IStep step) =>
        _steps[step.StepType] = step;
}

internal class CollectingEventSink : IEventSink
{
    public List<WorkflowEvent> Events { get; } = [];

    public Task PublishAsync(WorkflowEvent evt, CancellationToken cancellationToken = default)
    {
        Events.Add(evt);
        return Task.CompletedTask;
    }
}

#endregion

#region Test Steps

internal class MarkerStep : IStep
{
    public string StepType => "Marker";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var name = context.Inputs.GetValueOrDefault("name")?.ToString() ?? "marker";
        return Task.FromResult(StepResult.Success(new Dictionary<string, object?>
        {
            ["marked"] = true,
            ["name"] = name,
            ["timestamp"] = DateTimeOffset.UtcNow
        }));
    }
}

internal class DelayStep : IStep
{
    public string StepType => "Delay";

    public async Task<StepResult> ExecuteAsync(StepContext context)
    {
        var ms = context.Inputs.GetValueOrDefault("milliseconds") switch
        {
            int i => i,
            long l => (int)l,
            _ => 100
        };

        var label = context.Inputs.GetValueOrDefault("label")?.ToString() ?? "delay";

        await Task.Delay(ms, context.CancellationToken);

        return StepResult.Success(new Dictionary<string, object?>
        {
            ["delayed"] = ms,
            ["label"] = label
        });
    }
}

internal class AggregateStep : IStep
{
    public string StepType => "Aggregate";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        return Task.FromResult(StepResult.Success(new Dictionary<string, object?>
        {
            ["aggregated"] = true,
            ["contextKeys"] = context.State.Context.Keys.ToList()
        }));
    }
}

internal class FailingStep : IStep
{
    public string StepType => "Failing";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var message = context.Inputs.GetValueOrDefault("message")?.ToString() ?? "Step failed";
        return Task.FromResult(StepResult.Fail(message));
    }
}

internal class TrackingDelayStep : IStep
{
    private static int _currentConcurrent;
    private static int _maxConcurrent;
    private static readonly object Lock = new();

    public static int MaxConcurrent => _maxConcurrent;

    public static void Reset()
    {
        _currentConcurrent = 0;
        _maxConcurrent = 0;
    }

    public string StepType => "TrackingDelay";

    public async Task<StepResult> ExecuteAsync(StepContext context)
    {
        var ms = context.Inputs.GetValueOrDefault("milliseconds") switch
        {
            int i => i,
            long l => (int)l,
            _ => 100
        };

        lock (Lock)
        {
            _currentConcurrent++;
            if (_currentConcurrent > _maxConcurrent)
                _maxConcurrent = _currentConcurrent;
        }

        try
        {
            await Task.Delay(ms, context.CancellationToken);
            return StepResult.Success(new Dictionary<string, object?> { ["delayed"] = ms });
        }
        finally
        {
            lock (Lock) { _currentConcurrent--; }
        }
    }
}

internal class CountingStep : IStep
{
    private int _count;
    public string StepType => "Counting";
    public int ExecutionCount => _count;

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        Interlocked.Increment(ref _count);
        return Task.FromResult(StepResult.Success(new Dictionary<string, object?>
        {
            ["executionCount"] = _count
        }));
    }
}

#endregion