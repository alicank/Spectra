namespace Spectra.Contracts.Workflow;

/// <summary>
/// Controls whether agents can be revisited in a handoff chain.
/// </summary>
public class CyclePolicy
{
    /// <summary>Strictly deny revisiting any agent in the handoff chain.</summary>
    public static CyclePolicy Deny => new() { Mode = CyclePolicyMode.Deny };

    /// <summary>Allow unlimited revisits (dangerous — only for advanced users).</summary>
    public static CyclePolicy Allow => new() { Mode = CyclePolicyMode.Allow };

    /// <summary>Allow revisiting an agent up to the specified number of times.</summary>
    public static CyclePolicy AllowWithLimit(int maxRevisits) =>
        new() { Mode = CyclePolicyMode.AllowWithLimit, MaxRevisits = maxRevisits };

    public CyclePolicyMode Mode { get; init; } = CyclePolicyMode.Deny;
    public int MaxRevisits { get; init; } = 0;
}

public enum CyclePolicyMode
{
    Deny,
    Allow,
    AllowWithLimit
}