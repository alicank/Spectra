namespace Spectra.Contracts.Prompts;

/// <summary>
/// Controls how the renderer handles variables present in the template
/// but missing from the supplied dictionary.
/// </summary>
public enum MissingVariableBehavior
{
    /// <summary>Leave the {{placeholder}} text as-is in the output.</summary>
    LeaveTemplate,

    /// <summary>Throw a <see cref="KeyNotFoundException"/> for the first missing variable.</summary>
    ThrowException,

    /// <summary>Replace the placeholder with an empty string.</summary>
    ReplaceWithEmpty
}