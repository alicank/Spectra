using Spectra.Contracts.Mcp;

namespace Spectra.Kernel.Mcp;

/// <summary>
/// Tracks MCP tool call counts and estimated costs per workflow run.
/// Used by <see cref="McpToolAdapter"/> to enforce rate limits and budget guards.
/// Thread-safe for concurrent tool execution.
/// </summary>
public class McpCallTracker
{
    private readonly Dictionary<string, int> _callCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private decimal _estimatedCost;

    /// <summary>Total estimated cost accumulated across all MCP tool calls.</summary>
    public decimal EstimatedCost
    {
        get { lock (_lock) return _estimatedCost; }
    }

    /// <summary>
    /// Checks whether the next call to the given server/tool is allowed
    /// under the configured guardrails.
    /// </summary>
    /// <returns>Null if allowed; an error message if blocked.</returns>
    public string? CheckAllowed(McpServerConfig config, string toolName, decimal globalBudgetRemaining)
    {
        lock (_lock)
        {
            // Rate limit check
            if (config.MaxCallsPerSession > 0)
            {
                var key = $"{config.Name}";
                _callCounts.TryGetValue(key, out var count);
                if (count >= config.MaxCallsPerSession)
                    return $"MCP server '{config.Name}' has reached its maximum of " +
                           $"{config.MaxCallsPerSession} calls per session.";
            }

            // Budget check
            if (config.CostPerCall > 0 && globalBudgetRemaining > 0)
            {
                if (_estimatedCost + config.CostPerCall > globalBudgetRemaining)
                    return $"MCP call to '{toolName}' on '{config.Name}' would exceed " +
                           $"the remaining budget ({globalBudgetRemaining}).";
            }

            return null;
        }
    }

    /// <summary>
    /// Records a completed MCP tool call.
    /// </summary>
    public void RecordCall(McpServerConfig config, string toolName)
    {
        lock (_lock)
        {
            var key = $"{config.Name}";
            _callCounts.TryGetValue(key, out var count);
            _callCounts[key] = count + 1;

            _estimatedCost += config.CostPerCall;
        }
    }

    /// <summary>
    /// Gets the number of calls made to a specific server.
    /// </summary>
    public int GetCallCount(string serverName)
    {
        lock (_lock)
        {
            _callCounts.TryGetValue(serverName, out var count);
            return count;
        }
    }
}