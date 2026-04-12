namespace Spectra.Contracts.Providers.Fallback;

/// <summary>
/// Registry for named fallback policies.
/// </summary>
public interface IFallbackPolicyRegistry
{
    void Register(IFallbackPolicy policy);
    IFallbackPolicy? GetPolicy(string name);
}