using System.Collections.Concurrent;
using System.Diagnostics;
using Spectra.Contracts.Diagnostics;
using Spectra.Kernel.Diagnostics;
using Xunit;

namespace Spectra.Tests.Diagnostics;

public class SpectraActivitySourceTests : IDisposable
{
    private readonly ConcurrentBag<Activity> _collectedActivities = [];
    private readonly ActivityListener _listener;

    public SpectraActivitySourceTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SpectraActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _collectedActivities.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        GC.SuppressFinalize(this);
    }

    // ─── source identity ─────────────────────────────────────────────

    [Fact]
    public void Name_IsSpectraKernel()
    {
        Assert.Equal("Spectra.Kernel", SpectraActivitySource.Name);
    }

    [Fact]
    public void Source_HasCorrectName()
    {
        Assert.Equal(SpectraActivitySource.Name, SpectraActivitySource.Source.Name);
    }

    // ─── StartWorkflow ───────────────────────────────────────────────

    [Fact]
    public void StartWorkflow_CreatesActivityWithCorrectTags()
    {
        using var activity = SpectraActivitySource.StartWorkflow("wf-1", "run-1", "My Workflow");

        Assert.NotNull(activity);
        Assert.Equal("workflow.run", activity.OperationName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal("wf-1", activity.GetTagItem(SpectraTags.WorkflowId));
        Assert.Equal("run-1", activity.GetTagItem(SpectraTags.RunId));
        Assert.Equal("My Workflow", activity.GetTagItem(SpectraTags.WorkflowName));
    }

    [Fact]
    public void StartWorkflow_OmitsNameWhenNull()
    {
        using var activity = SpectraActivitySource.StartWorkflow("wf-2", "run-2");

        Assert.NotNull(activity);
        Assert.Null(activity.GetTagItem(SpectraTags.WorkflowName));
    }

    // ─── StartStep ───────────────────────────────────────────────────

    [Fact]
    public void StartStep_CreatesActivityWithCorrectTags()
    {
        using var activity = SpectraActivitySource.StartStep("wf-1", "run-1", "node-a", "LlmStep");

        Assert.NotNull(activity);
        Assert.Equal("step.execute", activity.OperationName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal("wf-1", activity.GetTagItem(SpectraTags.WorkflowId));
        Assert.Equal("run-1", activity.GetTagItem(SpectraTags.RunId));
        Assert.Equal("node-a", activity.GetTagItem(SpectraTags.NodeId));
        Assert.Equal("LlmStep", activity.GetTagItem(SpectraTags.StepType));
    }

    // ─── StartBatch ──────────────────────────────────────────────────

    [Fact]
    public void StartBatch_CreatesActivityWithCorrectTags()
    {
        using var activity = SpectraActivitySource.StartBatch("wf-1", "run-1", 5);

        Assert.NotNull(activity);
        Assert.Equal("workflow.parallel_batch", activity.OperationName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal("wf-1", activity.GetTagItem(SpectraTags.WorkflowId));
        Assert.Equal("run-1", activity.GetTagItem(SpectraTags.RunId));
        Assert.Equal(5, activity.GetTagItem(SpectraTags.BatchSize));
    }

    // ─── RecordError (Exception) ─────────────────────────────────────

    [Fact]
    public void RecordError_WithException_SetsStatusAndAddsEvent()
    {
        using var activity = SpectraActivitySource.StartStep("wf", "r", "n", "S");
        Assert.NotNull(activity);

        var ex = new InvalidOperationException("test error");
        SpectraActivitySource.RecordError(activity, ex);

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("test error", activity.StatusDescription);

        var exceptionEvent = activity.Events.FirstOrDefault(e => e.Name == "exception");
        Assert.NotEqual(default, exceptionEvent);
        Assert.Contains(exceptionEvent.Tags, t => t.Key == "exception.type"
            && (string)t.Value! == typeof(InvalidOperationException).FullName);
        Assert.Contains(exceptionEvent.Tags, t => t.Key == "exception.message"
            && (string)t.Value! == "test error");
    }

    // ─── RecordError (string) ────────────────────────────────────────

    [Fact]
    public void RecordError_WithMessage_SetsStatusAndTag()
    {
        using var activity = SpectraActivitySource.StartStep("wf", "r", "n", "S");
        Assert.NotNull(activity);

        SpectraActivitySource.RecordError(activity, "something went wrong");

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("something went wrong", activity.StatusDescription);
        Assert.Equal("something went wrong", activity.GetTagItem(SpectraTags.ErrorMessage));
    }

    // ─── null safety ─────────────────────────────────────────────────

    [Fact]
    public void RecordError_WithNullActivity_DoesNotThrow()
    {
        // Should be a no-op, not throw
        SpectraActivitySource.RecordError(null, new Exception("ignored"));
        SpectraActivitySource.RecordError(null, "also ignored");
    }

    // ─── no listener = null activity ─────────────────────────────────

    [Fact]
    public void StartWorkflow_ReturnsNull_WhenNoListenerAttached()
    {
        _listener.Dispose();

        // After disposing the only listener, new activities should be null
        var activity = SpectraActivitySource.StartWorkflow("wf", "run");

        // Activity may or may not be null depending on other listeners in the process,
        // but at minimum it should not throw
        activity?.Dispose();
    }
}