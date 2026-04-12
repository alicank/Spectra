namespace Spectra.Contracts.Workflow;

/// <summary>
/// Controls whether a supervisor agent is allowed to delegate work to worker agents.
/// </summary>
public enum DelegationPolicy
{
    /// <summary>Delegations proceed immediately.</summary>
    Allowed,

    /// <summary>Each delegation triggers an interrupt for approval.</summary>
    RequiresApproval,

    /// <summary>Delegations are disabled.</summary>
    Disabled
}