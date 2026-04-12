using System.Collections.Concurrent;
using Spectra.Contracts.Audit;

namespace Spectra.Kernel.Audit;

/// <summary>
/// In-memory audit store for development and testing.
/// Not suitable for production — entries are lost on process restart.
/// </summary>
public sealed class InMemoryAuditStore : IAuditStore
{
    private readonly ConcurrentBag<AuditEntry> _entries = [];

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> QueryByRunAsync(
        string runId, CancellationToken cancellationToken = default)
    {
        var results = _entries
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.Timestamp)
            .ToList();

        return Task.FromResult<IReadOnlyList<AuditEntry>>(results);
    }

    public Task<IReadOnlyList<AuditEntry>> QueryByTenantAsync(
        string tenantId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = _entries
            .Where(e => e.TenantId == tenantId && e.Timestamp >= from && e.Timestamp <= to)
            .OrderBy(e => e.Timestamp)
            .ToList();

        return Task.FromResult<IReadOnlyList<AuditEntry>>(results);
    }

    public Task<IReadOnlyList<AuditEntry>> QueryAsync(
        DateTimeOffset from, DateTimeOffset to, int maxResults = 1000,
        CancellationToken cancellationToken = default)
    {
        var results = _entries
            .Where(e => e.Timestamp >= from && e.Timestamp <= to)
            .OrderBy(e => e.Timestamp)
            .Take(maxResults)
            .ToList();

        return Task.FromResult<IReadOnlyList<AuditEntry>>(results);
    }

    /// <summary>Returns all entries for testing assertions.</summary>
    public IReadOnlyList<AuditEntry> GetAll() => _entries.ToList();
}