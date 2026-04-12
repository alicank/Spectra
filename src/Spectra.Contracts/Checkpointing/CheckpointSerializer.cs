using System.Text.Json;

namespace Spectra.Contracts.Checkpointing;

/// <summary>
/// Handles versioned serialization and deserialization of <see cref="Checkpoint"/> instances.
/// All store implementations should use this for consistent roundtrip behavior.
/// </summary>
public static class CheckpointSerializer
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Serializes a checkpoint to JSON, stamping the current schema version.
    /// </summary>
    public static string Serialize(Checkpoint checkpoint)
    {
        var versioned = checkpoint with { SchemaVersion = CurrentSchemaVersion };
        return JsonSerializer.Serialize(versioned, DefaultOptions);
    }

    /// <summary>
    /// Deserializes a checkpoint from JSON. Throws <see cref="CheckpointSchemaException"/>
    /// if the persisted schema version is newer than the running library version.
    /// </summary>
    public static Checkpoint Deserialize(string json)
    {
        var checkpoint = JsonSerializer.Deserialize<Checkpoint>(json, DefaultOptions)
            ?? throw new CheckpointSchemaException("Failed to deserialize checkpoint: result was null.");

        if (checkpoint.SchemaVersion > CurrentSchemaVersion)
        {
            throw new CheckpointSchemaException(
                $"Checkpoint schema version {checkpoint.SchemaVersion} is newer than the " +
                $"supported version {CurrentSchemaVersion}. Upgrade the Spectra package to read this checkpoint.");
        }

        return checkpoint;
    }

    /// <summary>
    /// Attempts to deserialize, returning null on failure instead of throwing.
    /// </summary>
    public static Checkpoint? TryDeserialize(string json)
    {
        try
        {
            return Deserialize(json);
        }
        catch
        {
            return null;
        }
    }
}