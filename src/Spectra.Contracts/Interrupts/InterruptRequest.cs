namespace Spectra.Contracts.Interrupts;

public sealed record InterruptRequest
{
    public required string RunId { get; init; }
    public required string WorkflowId { get; init; }
    public required string NodeId { get; init; }

    public string? Reason { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }

    public object? Payload { get; init; }

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>();
}