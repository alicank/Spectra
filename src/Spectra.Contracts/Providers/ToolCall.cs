namespace Spectra.Contracts.Providers;

public class ToolCall
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public Dictionary<string, object?> Arguments { get; set; } = new();
}