namespace Spectra.Contracts.Providers;

public class LlmResponse
{
    public required string Content { get; init; }
    public bool Success { get; init; } = true;
    public string? ErrorMessage { get; init; }

    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public TimeSpan? Latency { get; init; }

    public string? Model { get; init; }
    public string? StopReason { get; init; }

    public List<ToolCall>? ToolCalls { get; init; }
    public bool HasToolCalls => ToolCalls is { Count: > 0 };

    public static LlmResponse Error(string message) => new()
    {
        Content = string.Empty,
        Success = false,
        ErrorMessage = message
    };
}