namespace Spectra.Contracts.Audit;

/// <summary>
/// Persistent store for compliance audit entries.
/// Free tier ships <c>InMemoryAuditStore</c>; enterprise adds SQL Server, Cosmos DB,
/// tamper-evident hashing, retention policies, and GDPR export.
/// </summary>
public interface IAuditStore
{
    /// <summary>Writes an immutable audit entry.</summary>
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Queries audit entries for a specific workflow run.</summary>
    Task<IReadOnlyList<AuditEntry>> QueryByRunAsync(
        string runId,
        CancellationToken cancellationToken = default);

    /// <summary>Queries audit entries for a specific tenant within a time range.</summary>
    Task<IReadOnlyList<AuditEntry>> QueryByTenantAsync(
        string tenantId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    /// <summary>Queries all audit entries within a time range.</summary>
    Task<IReadOnlyList<AuditEntry>> QueryAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int maxResults = 1000,
        CancellationToken cancellationToken = default);
}