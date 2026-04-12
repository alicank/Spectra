namespace Spectra.Contracts.Steps;

/// <summary>
/// Controls when a session node considers its conversation complete
/// and allows the workflow to proceed to subsequent edges.
/// Multiple policies can be combined as flags.
/// </summary>
[Flags]
public enum SessionExitPolicy
{
    /// <summary>No automatic exit — session runs until externally killed.</summary>
    None = 0,

    /// <summary>The LLM signals completion via a special tool call (end_session).</summary>
    LlmDecides = 1,

    /// <summary>The user sends a recognized exit command (e.g. "/done").</summary>
    UserCommand = 2,

    /// <summary>A state condition expression evaluates to true.</summary>
    Condition = 4,

    /// <summary>The session has reached its maximum turn count.</summary>
    MaxTurns = 8,

    /// <summary>The cumulative token budget has been exhausted.</summary>
    TokenBudget = 16,

    /// <summary>The wall-clock timeout has elapsed since session start.</summary>
    Timeout = 32,

    /// <summary>An external API call explicitly ends the session.</summary>
    External = 64
}