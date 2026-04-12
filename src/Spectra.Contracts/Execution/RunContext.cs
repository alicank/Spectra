using System.Security.Claims;

namespace Spectra.Contracts.Execution;

/// <summary>
/// Caller-supplied identity and tenant context threaded through the entire
/// workflow pipeline. Spectra never authenticates — it just carries this POCO
/// like <c>HttpContext.User</c> carries a <see cref="ClaimsPrincipal"/>.
/// </summary>
public class RunContext
{
    /// <summary>Tenant identifier from the consumer's auth system.</summary>
    public string? TenantId { get; set; }

    /// <summary>User identifier from the consumer's JWT/claims.</summary>
    public string? UserId { get; set; }

    /// <summary>Pass-through claims from the caller's identity provider.</summary>
    public IEnumerable<Claim> Claims { get; set; } = [];

    /// <summary>Optional correlation ID for cross-system tracing.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Arbitrary metadata the consumer wants threaded through events and audit entries.</summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Convenience role list extracted from <see cref="Claims"/>.
    /// Consumers can populate this directly or let it be derived from claims.
    /// </summary>
    public IEnumerable<string> Roles { get; set; } = [];

    /// <summary>Returns <c>true</c> if <see cref="Roles"/> contains the specified role (case-insensitive).</summary>
    public bool HasRole(string role) =>
        Roles.Any(r => r.Equals(role, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns <c>true</c> if <see cref="Claims"/> contains a claim with the given type.</summary>
    public bool HasClaim(string type) =>
        Claims.Any(c => c.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns <c>true</c> if <see cref="Claims"/> contains a claim with the given type and value.</summary>
    public bool HasClaim(string type, string value) =>
        Claims.Any(c => c.Type.Equals(type, StringComparison.OrdinalIgnoreCase)
                     && c.Value.Equals(value, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds the first claim matching the given type, or <c>null</c>.</summary>
    public Claim? FindClaim(string type) =>
        Claims.FirstOrDefault(c => c.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    /// <summary>Anonymous context used when no identity is provided.</summary>
    public static readonly RunContext Anonymous = new();
}