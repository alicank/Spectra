namespace Spectra.Contracts.Workflow;

/// <summary>
/// Determines how much conversation context is transferred during a handoff.
/// </summary>
public enum ConversationScope
{
    /// <summary>Only the handoff payload (intent, constraints, data) is passed. Clean slate.</summary>
    Handoff,

    /// <summary>The full conversation history is transferred to the target agent.</summary>
    Full,

    /// <summary>A summary of the conversation is generated and passed to the target agent.</summary>
    Summary,

    /// <summary>The last N messages are transferred. N is configured via MaxContextMessages.</summary>
    LastN
}