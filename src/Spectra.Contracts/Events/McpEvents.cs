namespace Spectra.Contracts.Events;

/// <summary>
/// Emitted when an MCP server is successfully connected and tools are discovered.
/// </summary>
public sealed record McpServerConnectedEvent : WorkflowEvent
{
    public required string ServerName { get; init; }
    public required string Transport { get; init; }
    public required int ToolCount { get; init; }
    public required List<string> ToolNames { get; init; }
}

/// <summary>
/// Emitted when an MCP server disconnects (normally or due to failure).
/// </summary>
public sealed record McpServerDisconnectedEvent : WorkflowEvent
{
    public required string ServerName { get; init; }
    public required string Reason { get; init; }
}

/// <summary>
/// Emitted for each MCP tool invocation with MCP-specific tracing fields.
/// Fires in addition to <see cref="AgentToolCallEvent"/> which covers all tools generically.
/// </summary>
public sealed record McpToolCallEvent : WorkflowEvent
{
    public required string ServerName { get; init; }
    public required string ToolName { get; init; }
    public required string Transport { get; init; }
    public int? JsonRpcRequestId { get; init; }
    public TimeSpan Duration { get; init; }
    public bool Success { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int RetryCount { get; init; }
}

/// <summary>
/// Emitted when an MCP tool call is blocked by a guardrail
/// (rate limit, budget, read-only, denied tool, approval required).
/// </summary>
public sealed record McpToolCallBlockedEvent : WorkflowEvent
{
    public required string ServerName { get; init; }
    public required string ToolName { get; init; }
    public required string Reason { get; init; }
}