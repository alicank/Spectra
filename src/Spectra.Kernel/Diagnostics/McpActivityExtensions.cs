using System.Diagnostics;
using Spectra.Contracts.Diagnostics;

namespace Spectra.Kernel.Diagnostics;

/// <summary>
/// OTel activity helpers for MCP tool call tracing.
/// Extends <see cref="SpectraActivitySource"/> with MCP-specific span creation.
/// </summary>
public static class McpActivityExtensions
{
    /// <summary>
    /// Starts an MCP tool call span as a child of the current activity.
    /// </summary>
    public static Activity? StartMcpCall(
        string serverName,
        string toolName,
        string runId,
        string? nodeId = null)
    {
        var activity = SpectraActivitySource.Source.StartActivity("mcp.tool_call", ActivityKind.Client);
        if (activity is null) return null;

        activity.SetTag(SpectraTags.RunId, runId);
        activity.SetTag(McpTags.ServerName, serverName);
        activity.SetTag(McpTags.ToolName, toolName);

        if (nodeId is not null)
            activity.SetTag(SpectraTags.NodeId, nodeId);

        return activity;
    }

    /// <summary>
    /// Records MCP-specific completion tags on an activity.
    /// </summary>
    public static void RecordMcpCompletion(
        Activity? activity,
        bool success,
        TimeSpan duration,
        int retryCount,
        long? responseSizeBytes = null)
    {
        if (activity is null) return;

        activity.SetTag(McpTags.CallSuccess, success);
        activity.SetTag(McpTags.CallDuration, (long)duration.TotalMilliseconds);
        activity.SetTag(McpTags.RetryCount, retryCount);

        if (responseSizeBytes is not null)
            activity.SetTag(McpTags.ResponseSize, responseSizeBytes);

        if (!success)
            activity.SetStatus(ActivityStatusCode.Error);
    }
}