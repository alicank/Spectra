namespace Spectra.Contracts.Checkpointing;

/// <summary>
/// Thrown when a checkpoint cannot be deserialized due to an incompatible schema version.
/// </summary>
public sealed class CheckpointSchemaException : Exception
{
    public CheckpointSchemaException(string message) : base(message) { }
    public CheckpointSchemaException(string message, Exception innerException) : base(message, innerException) { }
}