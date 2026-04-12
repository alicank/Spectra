using System.Text.Json.Serialization;
using Spectra.Contracts.State;

namespace Spectra.Contracts.Checkpointing;

public sealed record Checkpoint
{
    public required string RunId { get; init; }

    public required string WorkflowId { get; init; }

    public required WorkflowState State { get; init; }

    public string? LastCompletedNodeId { get; init; }

    public string? NextNodeId { get; init; }

    public int StepsCompleted { get; init; }
    public int Index { get; init; }
    public string? ParentRunId { get; init; }

    /// <summary>Tenant identity captured at checkpoint time for scoped queries.</summary>
    public string? TenantId { get; init; }

    /// <summary>User identity captured at checkpoint time for audit correlation.</summary>
    public string? UserId { get; init; }

    public int? ParentCheckpointIndex { get; init; }

    public int SchemaVersion { get; init; } = 1;

    [JsonExtensionData]
    public Dictionary<string, object?>? ExtensionData { get; init; }

    /// <summary>
    /// When the checkpoint was created due to an interrupt, this holds
    /// the original request so it can be matched with a response on resume.
    /// </summary>
    public Interrupts.InterruptRequest? PendingInterrupt { get; init; }

    public CheckpointStatus Status { get; init; } = CheckpointStatus.InProgress;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum CheckpointStatus
{
    InProgress,
    Completed,
    Failed,
    Interrupted,
    AwaitingInput
}