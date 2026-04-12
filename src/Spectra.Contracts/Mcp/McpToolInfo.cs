using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectra.Contracts.Mcp;

/// <summary>
/// Represents a tool discovered from an MCP server via <c>tools/list</c>.
/// </summary>
public class McpToolInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("inputSchema")]
    public JsonElement? InputSchema { get; init; }

    [JsonPropertyName("annotations")]
    public McpToolAnnotations? Annotations { get; init; }
}

/// <summary>
/// MCP tool annotations providing metadata about tool behaviour.
/// Used for read/write scoping and human-in-the-loop decisions.
/// </summary>
public class McpToolAnnotations
{
    /// <summary>
    /// Human-readable title for the tool.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>
    /// Whether the tool performs only read operations (no side effects).
    /// </summary>
    [JsonPropertyName("readOnlyHint")]
    public bool ReadOnlyHint { get; init; } = false;

    /// <summary>
    /// Whether the tool may perform destructive operations.
    /// </summary>
    [JsonPropertyName("destructiveHint")]
    public bool DestructiveHint { get; init; } = false;

    /// <summary>
    /// Whether the tool is idempotent (safe to retry).
    /// </summary>
    [JsonPropertyName("idempotentHint")]
    public bool IdempotentHint { get; init; } = false;

    /// <summary>
    /// Whether the tool interacts with the outside world (network, file system).
    /// </summary>
    [JsonPropertyName("openWorldHint")]
    public bool OpenWorldHint { get; init; } = true;
}