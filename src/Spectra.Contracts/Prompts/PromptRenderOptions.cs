namespace Spectra.Contracts.Prompts;

/// <summary>
/// Options that control prompt rendering behavior.
/// </summary>
public class PromptRenderOptions
{
    public static readonly PromptRenderOptions Default = new();

    /// <summary>
    /// What to do when a variable in the template has no matching key in the dictionary.
    /// Defaults to <see cref="MissingVariableBehavior.LeaveTemplate"/>.
    /// </summary>
    public MissingVariableBehavior MissingVariableBehavior { get; init; } = MissingVariableBehavior.LeaveTemplate;
}