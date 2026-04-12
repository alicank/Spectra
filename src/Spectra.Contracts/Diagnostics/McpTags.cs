namespace Spectra.Contracts.Diagnostics;

/// <summary>
/// OpenTelemetry tag constants for MCP tool call tracing.
/// Extends <see cref="SpectraTags"/> with MCP-specific attributes.
/// </summary>
public static class McpTags
{
    public const string ServerName = "spectra.mcp.server.name";
    public const string ToolName = "spectra.mcp.tool.name";
    public const string Transport = "spectra.mcp.transport";
    public const string RequestId = "spectra.mcp.request.id";
    public const string CallDuration = "spectra.mcp.call.duration_ms";
    public const string CallSuccess = "spectra.mcp.call.success";
    public const string ErrorCode = "spectra.mcp.error.code";
    public const string RetryCount = "spectra.mcp.retry.count";
    public const string ResponseSize = "spectra.mcp.response.size_bytes";
    public const string CallsRemaining = "spectra.mcp.calls.remaining";
}