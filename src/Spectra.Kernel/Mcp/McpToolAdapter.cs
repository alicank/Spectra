using System.Diagnostics;
using Spectra.Contracts.Diagnostics;
using Spectra.Contracts.Events;
using Spectra.Contracts.Mcp;
using Spectra.Contracts.State;
using Spectra.Contracts.Tools;
using Spectra.Kernel.Diagnostics;

namespace Spectra.Kernel.Mcp;

/// <summary>
/// Wraps a single MCP tool as a native <see cref="ITool"/> instance.
/// Handles argument serialization, guardrail enforcement (rate limits, budget,
/// read-only, approval), response parsing, and OTel tracing.
/// </summary>
internal class McpToolAdapter : ITool
{
    private readonly McpClient _client;
    private readonly McpToolInfo _mcpTool;
    private readonly McpServerConfig _config;
    private readonly McpCallTracker _callTracker;
    private readonly IEventSink? _eventSink;

    public string Name { get; }
    public ToolDefinition Definition { get; }

    /// <summary>MCP annotations for read/write scoping decisions.</summary>
    public McpToolAnnotations? Annotations => _mcpTool.Annotations;

    /// <summary>The originating MCP server name.</summary>
    public string ServerName => _config.Name;

    /// <summary>The raw MCP tool name (without the mcp: prefix).</summary>
    public string McpToolName => _mcpTool.Name;

    public McpToolAdapter(
        McpClient client,
        McpToolInfo mcpTool,
        McpServerConfig config,
        McpCallTracker callTracker,
        IEventSink? eventSink = null)
    {
        _client = client;
        _mcpTool = mcpTool;
        _config = config;
        _callTracker = callTracker;
        _eventSink = eventSink;

        Definition = McpSchemaMapper.ToToolDefinition(mcpTool, config.Name);
        Name = Definition.Name;
    }

    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> arguments,
        WorkflowState state,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int retryCount = 0;

        // ── Guardrail: read-only enforcement ──
        if (_config.ReadOnly && _mcpTool.Annotations is { ReadOnlyHint: false })
        {
            await EmitBlockedEventAsync(state, "read_only_policy", ct);
            return ToolResult.Fail(
                $"Tool '{_mcpTool.Name}' on server '{_config.Name}' is blocked by read-only policy.");
        }

        // ── Guardrail: rate limit ──
        var budgetRemaining = GetGlobalBudgetRemaining(state);
        var blockReason = _callTracker.CheckAllowed(_config, _mcpTool.Name, budgetRemaining);
        if (blockReason is not null)
        {
            await EmitBlockedEventAsync(state, blockReason, ct);
            return ToolResult.Fail(blockReason);
        }

        // ── Guardrail: approval required ──
        if (_config.RequireApproval)
        {
            // TODO: Integrate with interrupt system once it supports tool-level interrupts.
            // For now, block and inform the LLM.
            await EmitBlockedEventAsync(state, "approval_required", ct);
            return ToolResult.Fail(
                $"Tool '{_mcpTool.Name}' on server '{_config.Name}' requires human approval. " +
                "Approval integration is pending.");
        }

        // ── Execute with resilience ──
        var resilience = _config.Resilience ?? new McpResilienceOptions();
        var maxAttempts = 1 + resilience.MaxRetries;
        Exception? lastException = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                retryCount = attempt;

                var delay = resilience.UseExponentialBackoff
                    ? TimeSpan.FromMilliseconds(
                        resilience.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1) +
                        resilience.BaseDelay.TotalMilliseconds * 0.25 * Random.Shared.NextDouble())
                    : resilience.BaseDelay;

                if (delay > resilience.MaxDelay)
                    delay = resilience.MaxDelay;

                await Task.Delay(delay, ct);
            }

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(resilience.Timeout);

                var rpcResponse = await _client.CallToolAsync(_mcpTool.Name, arguments, timeoutCts.Token);

                // Record the call for rate limiting
                _callTracker.RecordCall(_config, _mcpTool.Name);
                sw.Stop();

                // Emit MCP-specific event
                await EmitMcpEventAsync(
                    state,
                    sw.Elapsed,
                    rpcResponse.IsSuccess,
                    rpcResponse.Error?.Code,
                    rpcResponse.Error?.Message,
                    retryCount,
                    ct);

                if (!rpcResponse.IsSuccess)
                {
                    return ToolResult.Fail(
                        $"MCP tool '{_mcpTool.Name}' error: {rpcResponse.Error?.Message ?? "unknown"} " +
                        $"(code: {rpcResponse.Error?.Code})");
                }

                if (rpcResponse.Result is null)
                    return ToolResult.Ok("(empty result)");

                // Parse MCP tool result format
                var (content, isError) = McpSchemaMapper.ParseToolResult(rpcResponse.Result.Value);

                // Enforce response size limit
                if (content.Length > _config.MaxResponseSizeBytes)
                {
                    content = content[.._config.MaxResponseSizeBytes] +
                              $"\n...[truncated at {_config.MaxResponseSizeBytes} bytes]";
                }

                return isError
                    ? ToolResult.Fail(content)
                    : ToolResult.Ok(content);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastException = new TimeoutException(
                    $"MCP tool call '{_mcpTool.Name}' timed out after {resilience.Timeout.TotalSeconds}s " +
                    $"(attempt {attempt + 1}/{maxAttempts})");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastException = ex;
            }
        }

        sw.Stop();

        // All attempts failed
        await EmitMcpEventAsync(
            state,
            sw.Elapsed,
            false,
            null,
            lastException?.Message,
            retryCount,
            ct);

        return ToolResult.Fail(
            $"MCP tool '{_mcpTool.Name}' failed after {maxAttempts} attempts: " +
            $"{lastException?.Message ?? "unknown error"}");
    }

    private static decimal GetGlobalBudgetRemaining(WorkflowState state)
    {
        // Try to extract budget from agent execution context if present
        if (state.Context.TryGetValue("__agentExecutionContext", out var ctxObj)
            && ctxObj is Contracts.Workflow.AgentExecutionContext execCtx
            && execCtx.GlobalBudgetRemaining > 0)
        {
            return execCtx.GlobalBudgetRemaining;
        }

        return 0;
    }

    private async Task EmitMcpEventAsync(
        WorkflowState state,
        TimeSpan duration,
        bool success,
        int? errorCode,
        string? errorMessage,
        int retryCount,
        CancellationToken ct)
    {
        if (_eventSink is null) return;

        await _eventSink.PublishAsync(new McpToolCallEvent
        {
            RunId = state.RunId,
            WorkflowId = state.WorkflowId,
            EventType = "mcp.tool_call",
            ServerName = _config.Name,
            ToolName = _mcpTool.Name,
            Transport = _config.Transport.ToString().ToLowerInvariant(),
            Duration = duration,
            Success = success,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            RetryCount = retryCount
        }, ct);
    }

    private async Task EmitBlockedEventAsync(WorkflowState state, string reason, CancellationToken ct)
    {
        if (_eventSink is null) return;

        await _eventSink.PublishAsync(new McpToolCallBlockedEvent
        {
            RunId = state.RunId,
            WorkflowId = state.WorkflowId,
            EventType = "mcp.tool_call_blocked",
            ServerName = _config.Name,
            ToolName = _mcpTool.Name,
            Reason = reason
        }, ct);
    }
}