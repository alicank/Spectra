using Spectra.Contracts.Events;
using Spectra.Contracts.State;
using Spectra.Contracts.Tools;
using Spectra.Kernel.Execution;
using Spectra.Kernel.Resilience;
using Xunit;

namespace Spectra.Tests.Resilience;

public class ResilientToolDecoratorTests
{
    // --- Success passthrough ---

    [Fact]
    public async Task Success_PassesThroughResult()
    {
        var inner = new FakeTool("my-tool", ToolResult.Ok("hello"));
        var (sut, _, _) = CreateDecorator(inner);

        var result = await sut.ExecuteAsync([], CreateState());

        Assert.True(result.Success);
        Assert.Equal("hello", result.Content);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public Task Name_DelegatesToInner()
    {
        var inner = new FakeTool("my-tool", ToolResult.Ok("ok"));
        var (sut, _, _) = CreateDecorator(inner);

        Assert.Equal("my-tool", sut.Name);
        return Task.CompletedTask;
    }

    [Fact]
    public Task Definition_DelegatesToInner()
    {
        var inner = new FakeTool("my-tool", ToolResult.Ok("ok"));
        var (sut, _, _) = CreateDecorator(inner);

        Assert.Equal("my-tool", sut.Definition.Name);
        return Task.CompletedTask;
    }

    // --- Failure recording ---

    [Fact]
    public async Task Failure_RecordsInPolicy()
    {
        var inner = new FakeTool("my-tool", ToolResult.Fail("boom"));
        var (sut, policy, _) = CreateDecorator(inner);

        await sut.ExecuteAsync([], CreateState());

        var info = policy.GetInfo("my-tool");
        Assert.Equal(1, info.ConsecutiveFailures);
    }

    [Fact]
    public async Task Success_RecordsInPolicy()
    {
        var inner = new FakeTool("my-tool", ToolResult.Ok("ok"));
        var (sut, policy, _) = CreateDecorator(inner);

        // Record some failures first
        policy.RecordFailure("my-tool");

        await sut.ExecuteAsync([], CreateState());

        var info = policy.GetInfo("my-tool");
        Assert.Equal(0, info.ConsecutiveFailures);
    }

    // --- Circuit open → skip ---

    [Fact]
    public async Task OpenCircuit_SkipsExecutionAndReturnsError()
    {
        var inner = new FakeTool("my-tool", ToolResult.Ok("should not see this"));
        var (sut, policy, _) = CreateDecorator(inner, failureThreshold: 1);

        // Open the circuit
        policy.RecordFailure("my-tool");

        var result = await sut.ExecuteAsync([], CreateState());

        Assert.False(result.Success);
        Assert.Contains("circuit breaker", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, inner.CallCount); // tool was never called
    }

    // --- Fallback tool ---

    [Fact]
    public async Task OpenCircuit_UsesFallbackTool()
    {
        var inner = new FakeTool("my-tool", ToolResult.Ok("primary"));
        var fallback = new FakeTool("fallback-tool", ToolResult.Ok("fallback response"));

        var fallbackMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["my-tool"] = "fallback-tool"
        };

        var (sut, policy, _) = CreateDecorator(inner, failureThreshold: 1,
            fallbackTools: fallbackMapping, additionalTools: [fallback]);

        // Open the circuit
        policy.RecordFailure("my-tool");

        var result = await sut.ExecuteAsync([], CreateState());

        Assert.True(result.Success);
        Assert.Equal("fallback response", result.Content);
        Assert.Equal(0, inner.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    // --- Event emission ---

    [Fact]
    public async Task CircuitOpens_EmitsStateChangedEvent()
    {
        var inner = new FakeTool("my-tool", ToolResult.Fail("error"));
        var (sut, _, sink) = CreateDecorator(inner, failureThreshold: 1);

        await sut.ExecuteAsync([], CreateState());

        var stateChanges = sink.Events.OfType<ToolCircuitStateChangedEvent>().ToList();
        Assert.Single(stateChanges);
        Assert.Equal("my-tool", stateChanges[0].ToolName);
        Assert.Equal("Closed", stateChanges[0].PreviousState);
        Assert.Equal("Open", stateChanges[0].NewState);
    }

    [Fact]
    public async Task OpenCircuit_EmitsSkippedEvent()
    {
        var inner = new FakeTool("my-tool", ToolResult.Ok("ok"));
        var (sut, policy, sink) = CreateDecorator(inner, failureThreshold: 1);

        policy.RecordFailure("my-tool");
        sink.Events.Clear(); // clear the transition events

        await sut.ExecuteAsync([], CreateState());

        var skipped = sink.Events.OfType<ToolCallSkippedEvent>().ToList();
        Assert.Single(skipped);
        Assert.Equal("my-tool", skipped[0].ToolName);
        Assert.Equal("Open", skipped[0].CircuitState);
        Assert.False(skipped[0].FallbackUsed);
    }

    [Fact]
    public async Task OpenCircuit_WithFallback_EmitsSkippedEventWithFallbackInfo()
    {
        var inner = new FakeTool("my-tool", ToolResult.Ok("primary"));
        var fallback = new FakeTool("fallback-tool", ToolResult.Ok("fallback"));

        var fallbackMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["my-tool"] = "fallback-tool"
        };

        var (sut, policy, sink) = CreateDecorator(inner, failureThreshold: 1,
            fallbackTools: fallbackMapping, additionalTools: [fallback]);

        policy.RecordFailure("my-tool");
        sink.Events.Clear();

        await sut.ExecuteAsync([], CreateState());

        var skipped = sink.Events.OfType<ToolCallSkippedEvent>().ToList();
        Assert.Single(skipped);
        Assert.True(skipped[0].FallbackUsed);
        Assert.Equal("fallback-tool", skipped[0].FallbackToolName);
    }

    // --- Exception handling ---

    [Fact]
    public async Task ToolThrows_RecordsFailure()
    {
        var inner = new FakeTool("my-tool", new InvalidOperationException("tool crashed"));
        var (sut, policy, _) = CreateDecorator(inner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ExecuteAsync([], CreateState()));

        Assert.Equal(1, policy.GetInfo("my-tool").ConsecutiveFailures);
    }

    [Fact]
    public async Task CancellationException_IsNotCountedAsFailure()
    {
        var inner = new FakeTool("my-tool", new OperationCanceledException());
        var (sut, policy, _) = CreateDecorator(inner);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.ExecuteAsync([], CreateState()));

        Assert.Equal(0, policy.GetInfo("my-tool").ConsecutiveFailures);
    }

    // --- Recovery scenario ---

    [Fact]
    public async Task FullRecoveryScenario()
    {
        var callCount = 0;
        var inner = new FakeTool("my-tool", () =>
        {
            callCount++;
            return callCount <= 3
                ? ToolResult.Fail("transient error")
                : ToolResult.Ok("recovered");
        });

        var (sut, _, sink) = CreateDecorator(inner, failureThreshold: 3, cooldownMs: 1);

        // 3 failures → circuit opens
        await sut.ExecuteAsync([], CreateState());
        await sut.ExecuteAsync([], CreateState());
        await sut.ExecuteAsync([], CreateState());

        var openEvents = sink.Events.OfType<ToolCircuitStateChangedEvent>()
            .Where(e => e.NewState == "Open").ToList();
        Assert.Single(openEvents);

        // Wait for cooldown → half-open
        await Task.Delay(50);

        // Next call succeeds → circuit closes
        var result = await sut.ExecuteAsync([], CreateState());
        Assert.True(result.Success);
        Assert.Equal("recovered", result.Content);

        var closedEvents = sink.Events.OfType<ToolCircuitStateChangedEvent>()
            .Where(e => e.NewState == "Closed").ToList();
        Assert.Single(closedEvents);
    }

    // --- Audit trail integration ---

    [Fact]
    public async Task Events_IncludeRunAndWorkflowContext()
    {
        var inner = new FakeTool("my-tool", ToolResult.Fail("error"));
        var (sut, _, sink) = CreateDecorator(inner, failureThreshold: 1);

        var state = CreateState("run-123", "wf-456");
        await sut.ExecuteAsync([], state);

        var stateChange = sink.Events.OfType<ToolCircuitStateChangedEvent>().First();
        Assert.Equal("run-123", stateChange.RunId);
        Assert.Equal("wf-456", stateChange.WorkflowId);
    }

    // --- Helpers ---

    private static WorkflowState CreateState(string runId = "test-run", string workflowId = "test-wf")
    {
        return new WorkflowState
        {
            RunId = runId,
            WorkflowId = workflowId
        };
    }

    private static (ResilientToolDecorator Decorator, DefaultToolResiliencePolicy Policy, RecordingEventSink Sink)
        CreateDecorator(
            FakeTool inner,
            int failureThreshold = 5,
            int cooldownMs = 60_000,
            Dictionary<string, string>? fallbackTools = null,
            FakeTool[]? additionalTools = null)
    {
        var options = new ToolResilienceOptions
        {
            FailureThreshold = failureThreshold,
            CooldownPeriod = TimeSpan.FromMilliseconds(cooldownMs),
            FallbackTools = fallbackTools ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        var policy = new DefaultToolResiliencePolicy(options);
        var sink = new RecordingEventSink();
        var registry = new InMemoryToolRegistry();

        // Register the inner tool and any additional tools (fallbacks)
        registry.Register(inner);
        if (additionalTools is not null)
        {
            foreach (var tool in additionalTools)
                registry.Register(tool);
        }

        var decorator = new ResilientToolDecorator(inner, policy, registry, sink);
        return (decorator, policy, sink);
    }

    internal class FakeTool : ITool
    {
        private readonly Func<Task<ToolResult>> _handler;
        public int CallCount { get; private set; }
        public string Name { get; }

        public ToolDefinition Definition { get; }

        public FakeTool(string name, ToolResult result)
        {
            Name = name;
            Definition = new ToolDefinition { Name = name, Description = $"Fake tool: {name}" };
            _handler = () => Task.FromResult(result);
        }

        public FakeTool(string name, Func<ToolResult> resultFactory)
        {
            Name = name;
            Definition = new ToolDefinition { Name = name, Description = $"Fake tool: {name}" };
            _handler = () => Task.FromResult(resultFactory());
        }

        public FakeTool(string name, Exception exception)
        {
            Name = name;
            Definition = new ToolDefinition { Name = name, Description = $"Fake tool: {name}" };
            _handler = () => throw exception;
        }

        public async Task<ToolResult> ExecuteAsync(
            Dictionary<string, object?> arguments,
            WorkflowState state,
            CancellationToken ct = default)
        {
            CallCount++;
            return await _handler();
        }
    }

    private class RecordingEventSink : IEventSink
    {
        public List<WorkflowEvent> Events { get; } = [];

        public Task PublishAsync(WorkflowEvent evt, CancellationToken cancellationToken = default)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }
    }
}