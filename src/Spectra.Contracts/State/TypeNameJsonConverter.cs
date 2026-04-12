using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectra.Contracts.State;

/// <summary>
/// Converts <see cref="Type"/> to/from a simplified string representation for JSON serialization.
/// Supports common CLR types and generic collections used in workflow state definitions.
/// </summary>
public sealed class TypeNameJsonConverter : JsonConverter<Type>
{
    private static readonly Dictionary<string, Type> NameToType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["string"] = typeof(string),
        ["int"] = typeof(int),
        ["long"] = typeof(long),
        ["double"] = typeof(double),
        ["float"] = typeof(float),
        ["bool"] = typeof(bool),
        ["decimal"] = typeof(decimal),
        ["datetime"] = typeof(DateTime),
        ["object"] = typeof(object),
        ["list<string>"] = typeof(List<string>),
        ["list<int>"] = typeof(List<int>),
        ["list<double>"] = typeof(List<double>),
        ["list<object>"] = typeof(List<object>),
        ["dictionary<string,string>"] = typeof(Dictionary<string, string>),
        ["dictionary<string,object>"] = typeof(Dictionary<string, object>),
    };

    private static readonly Dictionary<Type, string> TypeToName =
        NameToType.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    public override Type Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var typeName = reader.GetString()
            ?? throw new JsonException("Type name cannot be null.");

        if (NameToType.TryGetValue(typeName, out var type))
            return type;

        throw new JsonException($"Unknown type name '{typeName}'. Supported types: {string.Join(", ", NameToType.Keys)}");
    }

    public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
    {
        if (TypeToName.TryGetValue(value, out var name))
        {
            writer.WriteStringValue(name);
            return;
        }

        throw new JsonException($"Type '{value.FullName}' is not supported for JSON serialization. Register it in {nameof(TypeNameJsonConverter)}.");
    }
}