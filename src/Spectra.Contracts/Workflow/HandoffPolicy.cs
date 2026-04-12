namespace Spectra.Contracts.Workflow;

/// <summary>
/// Controls whether an agent is allowed to initiate handoffs to other agents.
/// </summary>
public enum HandoffPolicy
{
    /// <summary>Handoffs proceed immediately without approval.</summary>
    Allowed,

    /// <summary>Handoffs trigger an interrupt for human/system approval before executing.</summary>
    RequiresApproval,

    /// <summary>Handoffs are disabled. The transfer tool returns an error to the LLM.</summary>
    Disabled
}