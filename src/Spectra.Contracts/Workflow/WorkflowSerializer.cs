using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectra.Contracts.Workflow;

/// <summary>
/// Handles serialization and deserialization of <see cref="WorkflowDefinition"/> to/from JSON.
/// Follows the same conventions as <see cref="Checkpointing.CheckpointSerializer"/>.
/// </summary>
public static class WorkflowSerializer
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new TimeSpanJsonConverter() }
    };

    /// <summary>Serializes a workflow definition to JSON.</summary>
    public static string Serialize(WorkflowDefinition definition)
        => JsonSerializer.Serialize(definition, DefaultOptions);

    /// <summary>Deserializes a workflow definition from JSON.</summary>
    public static WorkflowDefinition Deserialize(string json)
        => JsonSerializer.Deserialize<WorkflowDefinition>(json, DefaultOptions)
            ?? throw new InvalidOperationException("Failed to deserialize workflow definition: result was null.");

    /// <summary>Attempts to deserialize, returning null on failure instead of throwing.</summary>
    public static WorkflowDefinition? TryDeserialize(string json)
    {
        try { return Deserialize(json); }
        catch { return null; }
    }

    /// <summary>Deserializes a workflow definition from a stream.</summary>
    public static async Task<WorkflowDefinition> DeserializeAsync(Stream stream, CancellationToken ct = default)
        => await JsonSerializer.DeserializeAsync<WorkflowDefinition>(stream, DefaultOptions, ct)
            ?? throw new InvalidOperationException("Failed to deserialize workflow definition from stream.");
}

/// <summary>
/// Converts <see cref="TimeSpan"/> to/from an ISO 8601 duration-style string for JSON.
/// Format: "HH:MM:SS" (e.g., "00:05:00" for 5 minutes).
/// </summary>
internal sealed class TimeSpanJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString()
            ?? throw new JsonException("TimeSpan value cannot be null.");

        if (TimeSpan.TryParse(value, out var result))
            return result;

        throw new JsonException($"Cannot parse '{value}' as TimeSpan.");
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}