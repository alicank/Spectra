using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.State;
using Xunit;

namespace Spectra.Tests.Checkpointing;

public class CheckpointSerializerTests
{
    [Fact]
    public void Roundtrip_PreservesAllFields()
    {
        var checkpoint = new Checkpoint
        {
            RunId = "run-1",
            WorkflowId = "wf-1",
            State = new WorkflowState { WorkflowId = "wf-1", CurrentNodeId = "node-3" },
            LastCompletedNodeId = "node-2",
            NextNodeId = "node-3",
            StepsCompleted = 2,
            Status = CheckpointStatus.InProgress
        };

        var json = CheckpointSerializer.Serialize(checkpoint);
        var deserialized = CheckpointSerializer.Deserialize(json);

        Assert.Equal(checkpoint.RunId, deserialized.RunId);
        Assert.Equal(checkpoint.WorkflowId, deserialized.WorkflowId);
        Assert.Equal(checkpoint.LastCompletedNodeId, deserialized.LastCompletedNodeId);
        Assert.Equal(checkpoint.NextNodeId, deserialized.NextNodeId);
        Assert.Equal(checkpoint.StepsCompleted, deserialized.StepsCompleted);
        Assert.Equal(checkpoint.Status, deserialized.Status);
        Assert.Equal(CheckpointSerializer.CurrentSchemaVersion, deserialized.SchemaVersion);
    }

    [Fact]
    public void Roundtrip_PreservesCancelledStatus()
    {
        // CheckpointStatus.Cancelled was added after Completed/Failed/Interrupted/AwaitingInput.
        // This test guards against accidental enum ordering changes breaking persisted checkpoints.
        var checkpoint = new Checkpoint
        {
            RunId = "run-cancel",
            WorkflowId = "wf-cancel",
            State = new WorkflowState { WorkflowId = "wf-cancel" },
            StepsCompleted = 2,
            Status = CheckpointStatus.Cancelled
        };

        var json = CheckpointSerializer.Serialize(checkpoint);
        var deserialized = CheckpointSerializer.Deserialize(json);

        Assert.Equal(CheckpointStatus.Cancelled, deserialized.Status);
    }

    [Fact]
    public void Deserialize_FutureVersion_Throws()
    {
        var json = """
        {
            "runId": "run-1",
            "workflowId": "wf-1",
            "state": {},
            "schemaVersion": 999
        }
        """;

        Assert.Throws<CheckpointSchemaException>(() =>
            CheckpointSerializer.Deserialize(json));
    }

    [Fact]
    public void TryDeserialize_InvalidJson_ReturnsNull()
    {
        var result = CheckpointSerializer.TryDeserialize("not-json");

        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_MissingSchemaVersion_DefaultsToV1()
    {
        var json = """
        {
            "runId": "run-1",
            "workflowId": "wf-1",
            "state": {},
            "stepsCompleted": 0,
            "status": 0
        }
        """;

        var checkpoint = CheckpointSerializer.Deserialize(json);

        Assert.Equal(1, checkpoint.SchemaVersion);
    }
}