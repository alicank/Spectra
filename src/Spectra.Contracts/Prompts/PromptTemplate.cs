namespace Spectra.Contracts.Prompts;

public class PromptTemplate
{
    public required string Id { get; init; }
    public required string Content { get; init; }

    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Version { get; init; }
    public List<string> Variables { get; init; } = [];
    public Dictionary<string, object?> Metadata { get; init; } = [];

    public string? FilePath { get; init; }
    public DateTimeOffset? LoadedAt { get; init; }
}