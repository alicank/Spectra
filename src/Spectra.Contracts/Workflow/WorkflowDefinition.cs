using System.Text.Json.Serialization;
using Spectra.Contracts.State;

namespace Spectra.Contracts.Workflow;

public class WorkflowDefinition
{
    public required string Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int Version { get; set; } = 1;

    public string? EntryNodeId { get; set; }  // Starting point

    public List<NodeDefinition> Nodes { get; set; } = [];
    public List<EdgeDefinition> Edges { get; set; } = [];
    public List<AgentDefinition> Agents { get; set; } = [];
    public List<StateFieldDefinition> StateFields { get; set; } = [];
    public List<SubgraphDefinition> Subgraphs { get; set; } = [];

    // Global settings
    public int MaxConcurrency { get; set; } = 4;
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxNodeIterations { get; set; } = 100;

    // ── Multi-agent global guards ──

    /// <summary>
    /// Maximum total tokens allowed across all agents in a single workflow run.
    /// 0 = unlimited. Enforced by the runner.
    /// </summary>
    public int GlobalTokenBudget { get; set; }

    /// <summary>
    /// Maximum depth of handoff chains across the entire workflow.
    /// Acts as a ceiling over per-agent MaxHandoffChainDepth.
    /// </summary>
    public int MaxHandoffChainDepth { get; set; } = 10;

    /// <summary>
    /// Maximum total LLM iterations across all agent nodes in a single run.
    /// </summary>
    public int MaxTotalAgentIterations { get; set; } = 500;

}