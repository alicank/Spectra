using Spectra.Contracts.State;
using Spectra.Contracts.Tools;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Built-in tool injected into agents that have declared handoff targets.
/// When the LLM calls this tool, <see cref="AgentStep"/> intercepts the call
/// and converts it into a <see cref="Contracts.Workflow.AgentHandoff"/>.
/// The tool is never actually executed — it exists only to provide the LLM
/// with the tool definition so it knows the transfer capability exists.
/// </summary>
public class TransferToAgentTool : ITool
{
    private readonly List<string> _allowedTargets;

    public TransferToAgentTool(IEnumerable<string> allowedTargets)
    {
        _allowedTargets = allowedTargets.ToList();
    }

    public string Name => "transfer_to_agent";

    public ToolDefinition Definition => new()
    {
        Name = "transfer_to_agent",
        Description = BuildDescription(),
        Parameters =
        [
            new ToolParameter
            {
                Name = "target_agent",
                Description = $"The agent to transfer to. Must be one of: {string.Join(", ", _allowedTargets)}",
                Type = "string",
                Required = true
            },
            new ToolParameter
            {
                Name = "intent",
                Description = "The purpose of the handoff (e.g., 'implement', 'review', 'research', 'debug')",
                Type = "string",
                Required = true
            },
            new ToolParameter
            {
                Name = "context",
                Description = "Structured data or instructions to pass to the target agent",
                Type = "string",
                Required = false
            },
            new ToolParameter
            {
                Name = "constraints",
                Description = "Comma-separated list of constraints the target agent must respect",
                Type = "string",
                Required = false
            }
        ]
    };

    public Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> arguments,
        WorkflowState state,
        CancellationToken ct = default)
    {
        // This tool is never actually executed — AgentStep intercepts it.
        // If we reach here, something went wrong.
        return Task.FromResult(ToolResult.Fail(
            "transfer_to_agent should be intercepted by the agent framework. " +
            "This indicates an public error."));
    }

    private string BuildDescription()
    {
        var targets = string.Join(", ", _allowedTargets);
        return $"Transfer the conversation to another agent. " +
               $"Use this when the current task is better handled by a different specialist. " +
               $"Available targets: {targets}. " +
               $"Provide a clear intent and any relevant context for the target agent.";
    }
}