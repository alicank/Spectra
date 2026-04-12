using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.State;
using Spectra.Contracts.Threading;
using Xunit;

namespace Spectra.Tests.Threading;

/// <summary>
/// Contract compliance test suite for <see cref="IThreadManager"/> implementations.
/// Inherit this class, implement <see cref="CreateManager"/> and <see cref="CreateCheckpointStore"/>,
/// and all contract tests will run automatically.
/// </summary>
public abstract class ThreadManagerTestBase
{
    protected abstract IThreadManager CreateManager();
    protected abstract ICheckpointStore CreateCheckpointStore();

    private Contracts.Threading.Thread CreateThread(
        string? threadId = null,
        string? workflowId = null,
        string? tenantId = null,
        string? userId = null,
        string? label = null,
        IReadOnlyList<string>? tags = null)
    {
        var id = threadId ?? Guid.NewGuid().ToString();
        return new Contracts.Threading.Thread
        {
            ThreadId = id,
            WorkflowId = workflowId ?? "test-workflow",
            RunId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            UserId = userId,
            Label = label,
            Tags = tags ?? []
        };
    }

    [Fact]
    public async Task CreateAndGet_RoundTrips()
    {
        var mgr = CreateManager();
        var thread = CreateThread(label: "test thread");

        var created = await mgr.CreateAsync(thread);
        var loaded = await mgr.GetAsync(created.ThreadId);

        Assert.NotNull(loaded);
        Assert.Equal(thread.ThreadId, loaded!.ThreadId);
        Assert.Equal(thread.WorkflowId, loaded.WorkflowId);
        Assert.Equal(thread.RunId, loaded.RunId);
        Assert.Equal("test thread", loaded.Label);
    }

    [Fact]
    public async Task Get_NonExistent_ReturnsNull()
    {
        var mgr = CreateManager();
        var result = await mgr.GetAsync("does-not-exist");
        Assert.Null(result);
    }

    [Fact]
    public async Task Create_Duplicate_Throws()
    {
        var mgr = CreateManager();
        var thread = CreateThread();
        await mgr.CreateAsync(thread);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.CreateAsync(thread));
    }

    [Fact]
    public async Task Update_ChangesFields()
    {
        var mgr = CreateManager();
        var thread = CreateThread(label: "original");
        await mgr.CreateAsync(thread);

        var updated = thread with { Label = "modified" };
        var result = await mgr.UpdateAsync(updated);

        Assert.Equal("modified", result.Label);

        var loaded = await mgr.GetAsync(thread.ThreadId);
        Assert.Equal("modified", loaded!.Label);
    }

    [Fact]
    public async Task Update_NonExistent_Throws()
    {
        var mgr = CreateManager();
        var thread = CreateThread();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.UpdateAsync(thread));
    }

    [Fact]
    public async Task Delete_RemovesThread()
    {
        var mgr = CreateManager();
        var thread = CreateThread();
        await mgr.CreateAsync(thread);

        await mgr.DeleteAsync(thread.ThreadId);

        var loaded = await mgr.GetAsync(thread.ThreadId);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task Delete_NonExistent_DoesNotThrow()
    {
        var mgr = CreateManager();
        var ex = await Record.ExceptionAsync(() => mgr.DeleteAsync("nope"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task List_ReturnsAll_WhenNoFilter()
    {
        var mgr = CreateManager();
        await mgr.CreateAsync(CreateThread());
        await mgr.CreateAsync(CreateThread());

        var results = await mgr.ListAsync();
        Assert.True(results.Count >= 2);
    }

    [Fact]
    public async Task List_FiltersByTenantId()
    {
        var mgr = CreateManager();
        await mgr.CreateAsync(CreateThread(tenantId: "tenant-a"));
        await mgr.CreateAsync(CreateThread(tenantId: "tenant-b"));

        var results = await mgr.ListAsync(new ThreadFilter { TenantId = "tenant-a" });

        Assert.All(results, t => Assert.Equal("tenant-a", t.TenantId));
    }

    [Fact]
    public async Task List_FiltersByUserId()
    {
        var mgr = CreateManager();
        await mgr.CreateAsync(CreateThread(userId: "user-1"));
        await mgr.CreateAsync(CreateThread(userId: "user-2"));

        var results = await mgr.ListAsync(new ThreadFilter { UserId = "user-1" });

        Assert.All(results, t => Assert.Equal("user-1", t.UserId));
    }

    [Fact]
    public async Task List_FiltersByWorkflowId()
    {
        var mgr = CreateManager();
        await mgr.CreateAsync(CreateThread(workflowId: "wf-target"));
        await mgr.CreateAsync(CreateThread(workflowId: "wf-other"));

        var results = await mgr.ListAsync(new ThreadFilter { WorkflowId = "wf-target" });

        Assert.All(results, t => Assert.Equal("wf-target", t.WorkflowId));
    }

    [Fact]
    public async Task List_FiltersByTags()
    {
        var mgr = CreateManager();
        await mgr.CreateAsync(CreateThread(tags: ["alpha", "beta"]));
        await mgr.CreateAsync(CreateThread(tags: ["beta", "gamma"]));

        var results = await mgr.ListAsync(new ThreadFilter { Tags = ["alpha"] });

        Assert.All(results, t => Assert.Contains("alpha", t.Tags));
    }

    [Fact]
    public async Task List_FiltersByDateRange()
    {
        var mgr = CreateManager();
        var old = CreateThread() with { CreatedAt = DateTimeOffset.UtcNow.AddDays(-10) };
        var recent = CreateThread() with { CreatedAt = DateTimeOffset.UtcNow };

        await mgr.CreateAsync(old);
        await mgr.CreateAsync(recent);

        var results = await mgr.ListAsync(new ThreadFilter
        {
            CreatedAfter = DateTimeOffset.UtcNow.AddDays(-5)
        });

        Assert.DoesNotContain(results, t => t.ThreadId == old.ThreadId);
    }

    [Fact]
    public async Task Clone_CreatesNewThread()
    {
        var mgr = CreateManager();
        var source = CreateThread(label: "original", tenantId: "t1");
        await mgr.CreateAsync(source);

        var cloned = await mgr.CloneAsync(source.ThreadId);

        Assert.NotEqual(source.ThreadId, cloned.ThreadId);
        Assert.Equal(source.WorkflowId, cloned.WorkflowId);
        Assert.Equal(source.TenantId, cloned.TenantId);
        Assert.Equal(source.Label, cloned.Label);
        Assert.Equal(source.ThreadId, cloned.SourceThreadId);
    }

    [Fact]
    public async Task Clone_NonExistent_Throws()
    {
        var mgr = CreateManager();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.CloneAsync("does-not-exist"));
    }

    [Fact]
    public async Task BulkDelete_RemovesMatchingThreads()
    {
        var mgr = CreateManager();
        await mgr.CreateAsync(CreateThread(tenantId: "doomed"));
        await mgr.CreateAsync(CreateThread(tenantId: "doomed"));
        await mgr.CreateAsync(CreateThread(tenantId: "safe"));

        var deleted = await mgr.BulkDeleteAsync(new ThreadFilter { TenantId = "doomed" });

        Assert.Equal(2, deleted);
        var remaining = await mgr.ListAsync();
        Assert.All(remaining, t => Assert.NotEqual("doomed", t.TenantId));
    }

    [Fact]
    public async Task ApplyRetentionPolicy_DeletesOldThreads()
    {
        var mgr = CreateManager();
        var oldThread = CreateThread() with
        {
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
        var recentThread = CreateThread() with
        {
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await mgr.CreateAsync(oldThread);
        await mgr.CreateAsync(recentThread);

        var result = await mgr.ApplyRetentionPolicyAsync(new RetentionPolicy
        {
            MaxAge = TimeSpan.FromDays(7)
        });

        Assert.True(result.ThreadsDeleted >= 1);
        var remaining = await mgr.GetAsync(recentThread.ThreadId);
        Assert.NotNull(remaining);
    }
}