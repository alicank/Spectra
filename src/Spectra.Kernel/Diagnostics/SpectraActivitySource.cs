using System.Diagnostics;
using Spectra.Contracts.Diagnostics;

namespace Spectra.Kernel.Diagnostics;

/// <summary>
/// Central <see cref="ActivitySource"/> for the Spectra workflow engine.
/// Consumers wire this up to an OpenTelemetry TracerProvider via
/// <c>.AddSource(SpectraActivitySource.Name)</c>.
/// </summary>
public static class SpectraActivitySource
{
    public const string Name = "Spectra.Kernel";
    public const string Version = "1.0.0";

    public static readonly ActivitySource Source = new(Name, Version);

    /// <summary>
    /// Starts a workflow-level span. Returns null when no listener is attached.
    /// </summary>
    public static Activity? StartWorkflow(string workflowId, string runId, string? workflowName = null)
    {
        var activity = Source.StartActivity("workflow.run", ActivityKind.Internal);
        if (activity is null) return null;

        activity.SetTag(SpectraTags.WorkflowId, workflowId);
        activity.SetTag(SpectraTags.RunId, runId);
        if (workflowName is not null)
            activity.SetTag(SpectraTags.WorkflowName, workflowName);

        return activity;
    }

    /// <summary>
    /// Starts a step-level span nested under the current activity.
    /// </summary>
    public static Activity? StartStep(string workflowId, string runId, string nodeId, string stepType)
    {
        var activity = Source.StartActivity("step.execute", ActivityKind.Internal);
        if (activity is null) return null;

        activity.SetTag(SpectraTags.WorkflowId, workflowId);
        activity.SetTag(SpectraTags.RunId, runId);
        activity.SetTag(SpectraTags.NodeId, nodeId);
        activity.SetTag(SpectraTags.StepType, stepType);

        return activity;
    }

    /// <summary>
    /// Starts a parallel-batch span.
    /// </summary>
    public static Activity? StartBatch(string workflowId, string runId, int batchSize)
    {
        var activity = Source.StartActivity("workflow.parallel_batch", ActivityKind.Internal);
        if (activity is null) return null;

        activity.SetTag(SpectraTags.WorkflowId, workflowId);
        activity.SetTag(SpectraTags.RunId, runId);
        activity.SetTag(SpectraTags.BatchSize, batchSize);

        return activity;
    }

    /// <summary>
    /// Starts a tool execution span with circuit breaker state tracking.
    /// Nested under the current step or workflow span for distributed trace continuity.
    /// </summary>
    public static Activity? StartToolExecution(string toolName)
    {
        var activity = Source.StartActivity("tool.execute", ActivityKind.Internal);
        if (activity is null) return null;

        activity.SetTag(SpectraTags.ToolName, toolName);

        return activity;
    }

    /// <summary>
    /// Records an error on the current activity following OTel conventions.
    /// </summary>
    public static void RecordError(Activity? activity, Exception ex)
    {
        if (activity is null) return;

        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            { "exception.type", ex.GetType().FullName },
            { "exception.message", ex.Message },
            { "exception.stacktrace", ex.StackTrace }
        }));
    }

    /// <summary>
    /// Records an error message on the current activity.
    /// </summary>
    public static void RecordError(Activity? activity, string message)
    {
        if (activity is null) return;

        activity.SetStatus(ActivityStatusCode.Error, message);
        activity.SetTag(SpectraTags.ErrorMessage, message);
    }
}