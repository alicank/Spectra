namespace Spectra.Contracts.Diagnostics;

/// <summary>
/// OpenTelemetry-compatible tag name constants for Spectra workflow tracing.
/// Follows OTel semantic conventions where applicable.
/// </summary>
public static class SpectraTags
{
    public const string WorkflowId = "spectra.workflow.id";
    public const string WorkflowName = "spectra.workflow.name";
    public const string RunId = "spectra.run.id";
    public const string NodeId = "spectra.node.id";
    public const string StepType = "spectra.step.type";
    public const string StepStatus = "spectra.step.status";
    public const string StepsExecuted = "spectra.steps.executed";
    public const string TotalNodes = "spectra.workflow.total_nodes";
    public const string ErrorMessage = "spectra.error.message";
    public const string BatchSize = "spectra.batch.size";
    public const string BatchSuccessCount = "spectra.batch.success_count";
    public const string BatchFailureCount = "spectra.batch.failure_count";
    public const string InterruptReason = "spectra.interrupt.reason";
    public const string InterruptIsDeclarative = "spectra.interrupt.declarative";
    public const string CheckpointStatus = "spectra.checkpoint.status";
    public const string ResumedFromNodeId = "spectra.resumed_from.node_id";
    public const string ForkedFromRunId = "spectra.forked_from.run_id";
    public const string ForkedFromCheckpointIndex = "spectra.forked_from.checkpoint_index";

    // ── Tool circuit breaker ──
    public const string ToolName = "spectra.tool.name";
    public const string ToolCircuitState = "spectra.tool.circuit_state";
    public const string ToolCircuitFailureCount = "spectra.tool.circuit_failure_count";
    public const string ToolFallbackUsed = "spectra.tool.fallback_used";
    public const string ToolFallbackName = "spectra.tool.fallback_name";

}