using Spectra.Contracts.Execution;
using Spectra.Contracts.Memory;
using System.Diagnostics;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.State;
using Spectra.Contracts.Workflow;

namespace Spectra.Contracts.Steps;

public class StepContext
{
    public required string RunId { get; init; }
    public required string WorkflowId { get; init; }
    public required string NodeId { get; init; }
    public required WorkflowState State { get; init; }
    public required CancellationToken CancellationToken { get; init; }

    public Dictionary<string, object?> Inputs { get; init; } = [];

    public IServiceProvider? Services { get; init; }

    /// <summary>
    /// Caller-supplied identity and tenant context.
    /// Defaults to <see cref="RunContext.Anonymous"/> when not provided.
    /// </summary>
    public RunContext RunContext { get; init; } = RunContext.Anonymous;

    /// <summary>
    /// The workflow definition currently being executed.
    /// Used by subgraph steps to locate child workflow definitions.
    /// </summary>
    public WorkflowDefinition? WorkflowDefinition { get; init; }

    /// <summary>
    /// Optional long-term memory store for cross-session persistence.
    /// Steps can use this to recall or store knowledge that outlives a single run.
    /// </summary>
    public IMemoryStore? Memory { get; init; }


    /// <summary>
    /// Optional callback for streaming token deltas to the workflow runner.
    /// Steps that support streaming should invoke this for each token chunk.
    /// When null, the step is not being executed in streaming mode.
    /// </summary>
    public Func<string, CancellationToken, Task>? OnToken { get; init; }

    /// <summary>
    /// Whether this step execution is in streaming mode.
    /// </summary>
    public bool IsStreaming => OnToken is not null;

    /// <summary>
    /// The current <see cref="System.Diagnostics.Activity"/> for this step execution.
    /// Steps can use this to add custom tags, events, or child spans.
    /// Returns <c>null</c> when no tracing listener is attached.
    /// </summary>
    public Activity? TracingActivity => Activity.Current;
    public Func<InterruptRequest, CancellationToken, Task<InterruptResponse>>? Interrupt { get; init; }

    /// <summary>
    /// Sends an interrupt request to the configured handler.
    /// If no handler is configured, throws <see cref="InterruptException"/>
    /// which the runner catches to checkpoint and suspend execution.
    /// </summary>
    public async Task<InterruptResponse> InterruptAsync(InterruptRequest request)
    {
        if (Interrupt is null)
            throw new InterruptException(request);

        return await Interrupt(request, CancellationToken);
    }

    /// <summary>
    /// Fluent overload: pauses execution with a reason and optional configuration.
    /// <code>
    /// var response = await context.InterruptAsync("review-needed", b => b
    ///     .WithTitle("Review Required")
    ///     .WithPayload(data));
    /// </code>
    /// </summary>
    public Task<InterruptResponse> InterruptAsync(
        string reason,
        Action<InterruptRequestBuilder>? configure = null)
    {
        var builder = new InterruptRequestBuilder(RunId, WorkflowId, NodeId, reason);
        configure?.Invoke(builder);
        return InterruptAsync(builder.Build());
    }
}