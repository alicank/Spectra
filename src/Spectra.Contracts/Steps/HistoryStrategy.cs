namespace Spectra.Contracts.Steps;

/// <summary>
/// Determines how conversation history is managed within a session node.
/// </summary>
public enum HistoryStrategy
{
    /// <summary>Keep all messages. Simple but may cause state bloat for long conversations.</summary>
    Full,

    /// <summary>Keep only the last N messages (sliding window). N is configured via MaxHistoryMessages.</summary>
    SlidingWindow,

    /// <summary>Periodically summarize older messages via an LLM call (future — not yet implemented).</summary>
    Summarize
}