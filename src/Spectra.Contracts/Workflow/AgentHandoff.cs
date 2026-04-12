using Spectra.Contracts.Providers;

namespace Spectra.Contracts.Workflow;

/// <summary>
/// Represents a structured handoff between agents in a multi-agent workflow.
/// Produced when an agent calls the <c>transfer_to_agent</c> tool.
/// </summary>
public class AgentHandoff
{
    public string FromAgent { get; set; } = string.Empty;
    public string ToAgent { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public Dictionary<string, object?> Payload { get; set; } = new();
    public List<string> Constraints { get; set; } = [];

    /// <summary>
    /// Conversation messages to transfer to the target agent.
    /// Populated based on the <see cref="ConversationScope"/> configured on the source agent.
    /// </summary>
    public List<LlmMessage>? TransferredMessages { get; set; }

    /// <summary>
    /// The conversation scope that was applied when building <see cref="TransferredMessages"/>.
    /// </summary>
    public ConversationScope ConversationScope { get; set; } = ConversationScope.Handoff;
}