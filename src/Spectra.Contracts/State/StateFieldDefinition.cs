using System.Text.Json.Serialization;

namespace Spectra.Contracts.State;

public sealed class StateFieldDefinition
{
    public required string Path { get; init; }         // e.g. "Context.Messages"

    [JsonConverter(typeof(TypeNameJsonConverter))]
    public required Type ValueType { get; init; }      // e.g. typeof(List<string>)

    public string? ReducerKey { get; init; }           // e.g. "append"
}