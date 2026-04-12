using Spectra.Contracts.Providers.Fallback;

namespace Spectra.Kernel.Resilience;

/// <summary>
/// Default in-memory implementation of <see cref="IFallbackPolicyRegistry"/>.
/// </summary>
internal sealed class InMemoryFallbackPolicyRegistry : IFallbackPolicyRegistry
{
    private readonly Dictionary<string, IFallbackPolicy> _policies = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IFallbackPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policies[policy.Name] = policy;
    }

    public IFallbackPolicy? GetPolicy(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _policies.TryGetValue(name, out var policy) ? policy : null;
    }
}