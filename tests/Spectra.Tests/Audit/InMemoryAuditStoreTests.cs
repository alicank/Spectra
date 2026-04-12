using Spectra.Contracts.Audit;
using Spectra.Kernel.Audit;
using Xunit;

namespace Spectra.Tests.Audit;

public class InMemoryAuditStoreTests
{
    private readonly InMemoryAuditStore _store = new();

    private static AuditEntry CreateEntry(
        string runId = "run-1",
        string tenantId = "tenant-1",
        string eventType = "StepCompleted",
        DateTimeOffset? timestamp = null) => new()
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            TenantId = tenantId,
            UserId = "user-1",
            RunId = runId,
            WorkflowId = "wf-1",
            EventType = eventType,
            EventData = "{}"
        };

    [Fact]
    public async Task WriteAsync_stores_entry()
    {
        var entry = CreateEntry();

        await _store.WriteAsync(entry);

        var results = _store.GetAll();
        Assert.Single(results);
        Assert.Equal(entry.Id, results[0].Id);
    }

    [Fact]
    public async Task QueryByRunAsync_filters_by_run_id()
    {
        await _store.WriteAsync(CreateEntry(runId: "run-1"));
        await _store.WriteAsync(CreateEntry(runId: "run-2"));
        await _store.WriteAsync(CreateEntry(runId: "run-1"));

        var results = await _store.QueryByRunAsync("run-1");

        Assert.Equal(2, results.Count);
        Assert.All(results, e => Assert.Equal("run-1", e.RunId));
    }

    [Fact]
    public async Task QueryByTenantAsync_filters_by_tenant_and_time_range()
    {
        var baseTime = DateTimeOffset.UtcNow;
        await _store.WriteAsync(CreateEntry(tenantId: "t1", timestamp: baseTime.AddMinutes(-10)));
        await _store.WriteAsync(CreateEntry(tenantId: "t1", timestamp: baseTime));
        await _store.WriteAsync(CreateEntry(tenantId: "t2", timestamp: baseTime));

        var results = await _store.QueryByTenantAsync("t1",
            baseTime.AddMinutes(-5), baseTime.AddMinutes(5));

        Assert.Single(results);
        Assert.Equal("t1", results[0].TenantId);
    }

    [Fact]
    public async Task QueryAsync_respects_max_results()
    {
        for (var i = 0; i < 5; i++)
            await _store.WriteAsync(CreateEntry());

        var from = DateTimeOffset.UtcNow.AddMinutes(-1);
        var to = DateTimeOffset.UtcNow.AddMinutes(1);

        var results = await _store.QueryAsync(from, to, maxResults: 3);

        Assert.Equal(3, results.Count);
    }
}