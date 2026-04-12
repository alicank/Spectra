namespace Spectra.Contracts.Mcp;

/// <summary>
/// Configuration for a single MCP server connection.
/// Supports stdio, SSE, and streamable-HTTP transports.
/// </summary>
public class McpServerConfig
{
    /// <summary>
    /// Logical name for this MCP server (e.g., "filesystem", "github").
    /// Used as the namespace prefix in tool naming: <c>mcp:{Name}:{toolName}</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Transport type. Determines which transport implementation is created.
    /// </summary>
    public McpTransportType Transport { get; init; } = McpTransportType.Stdio;

    // ── Stdio transport settings ──

    /// <summary>Command to execute (e.g., "npx", "node", "python").</summary>
    public string? Command { get; init; }

    /// <summary>Arguments passed to the command.</summary>
    public List<string> Arguments { get; init; } = [];

    /// <summary>Working directory for the spawned process.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Environment variables to set on the child process.
    /// Only these variables (plus system defaults) are passed — the full parent
    /// environment is NOT inherited unless <see cref="InheritEnvironment"/> is true.
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; init; } = new();

    /// <summary>
    /// When true, the child process inherits the parent's full environment.
    /// When false (default), only <see cref="EnvironmentVariables"/> are set.
    /// Enterprise deployments should keep this false to prevent credential leakage.
    /// </summary>
    public bool InheritEnvironment { get; init; } = false;

    // ── HTTP / SSE transport settings ──

    /// <summary>URL for SSE or streamable-HTTP endpoints.</summary>
    public string? Url { get; init; }

    /// <summary>Additional HTTP headers (e.g., Authorization).</summary>
    public Dictionary<string, string> Headers { get; init; } = new();

    // ── Guardrails ──

    /// <summary>
    /// Tool names this server is allowed to expose. Null = all tools allowed.
    /// Takes precedence before <see cref="DeniedTools"/>.
    /// </summary>
    public List<string>? AllowedTools { get; init; }

    /// <summary>
    /// Tool names explicitly blocked from this server.
    /// Applied after <see cref="AllowedTools"/> filtering.
    /// </summary>
    public List<string>? DeniedTools { get; init; }

    /// <summary>
    /// When true, only tools whose MCP annotations indicate read-only behaviour
    /// are exposed. Write/destructive tools are filtered out.
    /// </summary>
    public bool ReadOnly { get; init; } = false;

    /// <summary>
    /// Maximum number of calls to this server per workflow run. 0 = unlimited.
    /// Prevents runaway agentic loops from flooding external services.
    /// </summary>
    public int MaxCallsPerSession { get; init; } = 0;

    /// <summary>
    /// Maximum number of concurrent calls to this server within a single
    /// agentic iteration. 0 = unlimited (bounded only by the tool call batch size).
    /// </summary>
    public int MaxConcurrentCalls { get; init; } = 0;

    /// <summary>
    /// Maximum allowed response payload size in bytes. Responses exceeding this
    /// are truncated. Default 1 MB.
    /// </summary>
    public int MaxResponseSizeBytes { get; init; } = 1_048_576;

    /// <summary>
    /// Estimated cost per tool call (in arbitrary units matching the workflow budget).
    /// Used for pre-flight budget checks. 0 = free.
    /// </summary>
    public decimal CostPerCall { get; init; } = 0;

    /// <summary>
    /// When true, every tool call to this server triggers an interrupt for
    /// human approval before execution. For high-risk MCP servers.
    /// </summary>
    public bool RequireApproval { get; init; } = false;

    /// <summary>
    /// Resilience settings for this MCP server (timeout, retry, backoff).
    /// When null, defaults are used.
    /// </summary>
    public McpResilienceOptions? Resilience { get; init; }
}

/// <summary>
/// MCP transport type.
/// </summary>
public enum McpTransportType
{
    /// <summary>Spawn a child process and communicate over stdin/stdout.</summary>
    Stdio,

    /// <summary>Connect to an HTTP endpoint using Server-Sent Events.</summary>
    Sse,

    /// <summary>Connect using the streamable-HTTP transport (POST with optional SSE response).</summary>
    Http
}