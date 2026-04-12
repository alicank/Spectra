using Spectra.Contracts.Prompts;

namespace Spectra.Kernel.Prompts;

/// <summary>
/// Default in-memory implementation of <see cref="IPromptRegistry"/>.
/// Used as the fallback when no prompt directory is configured.
/// </summary>
public sealed class InMemoryPromptRegistry : IPromptRegistry
{
    private readonly Dictionary<string, PromptTemplate> _prompts = new(StringComparer.OrdinalIgnoreCase);

    public PromptTemplate? GetPrompt(string promptId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptId);
        return _prompts.GetValueOrDefault(promptId);
    }

    public IReadOnlyList<PromptTemplate> GetAll()
        => _prompts.Values.ToList().AsReadOnly();

    public void Register(PromptTemplate prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        _prompts[prompt.Id] = prompt;
    }

    public void Reload() { /* No-op for in-memory registry */ }
}