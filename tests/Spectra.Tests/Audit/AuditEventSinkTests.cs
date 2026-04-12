using Spectra.Contracts.Audit;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Kernel.Audit;
using Xunit;

namespace Spectra.Tests.Audit;

public class AuditEventSinkTests
{
    private readonly InMemoryAuditStore _store = new();

    [Fact]
    public async Task PublishAsync_writes_audit_entry_with_identity()
    {
        var runContext = new RunContext
        {
            TenantId = "acme-corp",
            UserId = "user-42"
        };
        var sink = new AuditEventSink(_store, () => runContext);

        var evt = new WorkflowStartedEvent
        {
            RunId = "run-1",
            WorkflowId = "wf-1",
            EventType = nameof(WorkflowStartedEvent),
            WorkflowName = "Test",
            TotalNodes = 3
        };

        await sink.PublishAsync(evt);

        var entries = _store.GetAll();
        Assert.Single(entries);
        var entry = entries[0];
        Assert.Equal("acme-corp", entry.TenantId);
        Assert.Equal("user-42", entry.UserId);
        Assert.Equal("run-1", entry.RunId);
        Assert.Equal(nameof(WorkflowStartedEvent), entry.EventType);
    }

    [Fact]
    public async Task PublishAsync_uses_anonymous_when_no_context()
    {
        var sink = new AuditEventSink(_store, () => null);

        var evt = new StepCompletedEvent
        {
            RunId = "run-1",
            WorkflowId = "wf-1",
            NodeId = "node-1",
            EventType = nameof(StepCompletedEvent),
            StepType = "prompt",
            Status = Contracts.Steps.StepStatus.Succeeded
        };

        await sink.PublishAsync(evt);

        var entries = _store.GetAll();
        Assert.Single(entries);
        Assert.Null(entries[0].TenantId);
        Assert.Null(entries[0].UserId);
    }

    [Fact]
    public async Task PublishAsync_captures_event_data_as_json()
    {
        var sink = new AuditEventSink(_store, () => RunContext.Anonymous);

        var evt = new StepCompletedEvent
        {
            RunId = "run-1",
            WorkflowId = "wf-1",
            NodeId = "n1",
            EventType = nameof(StepCompletedEvent),
            StepType = "agent",
            Status = Contracts.Steps.StepStatus.Succeeded,
            Duration = TimeSpan.FromSeconds(2)
        };

        await sink.PublishAsync(evt);

        var entries = _store.GetAll();
        Assert.NotNull(entries[0].EventData);
        Assert.Contains("StepCompletedEvent", entries[0].EventData);
    }

    [Fact]
    public void Ctor_throws_on_null_store()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AuditEventSink(null!, () => RunContext.Anonymous));
    }

    [Fact]
    public void Ctor_throws_on_null_accessor()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AuditEventSink(_store, null!));
    }
}