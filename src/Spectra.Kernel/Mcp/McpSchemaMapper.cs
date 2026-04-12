using System.Text.Json;
using Spectra.Contracts.Mcp;
using Spectra.Contracts.Tools;

namespace Spectra.Kernel.Mcp;

/// <summary>
/// Maps MCP tool JSON Schema definitions to Spectra <see cref="ToolDefinition"/> and
/// converts Spectra argument dictionaries to/from MCP JSON payloads.
/// </summary>
internal static class McpSchemaMapper
{
    /// <summary>
    /// Converts an <see cref="McpToolInfo"/> to a Spectra <see cref="ToolDefinition"/>.
    /// Flattens top-level JSON Schema properties into <see cref="ToolParameter"/> entries.
    /// </summary>
    public static ToolDefinition ToToolDefinition(McpToolInfo mcpTool, string serverName)
    {
        var parameters = new List<ToolParameter>();
        var requiredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (mcpTool.InputSchema is { } schema && schema.ValueKind == JsonValueKind.Object)
        {
            // Extract "required" array
            if (schema.TryGetProperty("required", out var requiredProp)
                && requiredProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in requiredProp.EnumerateArray())
                {
                    if (item.GetString() is { } req)
                        requiredSet.Add(req);
                }
            }

            // Extract "properties"
            if (schema.TryGetProperty("properties", out var props)
                && props.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in props.EnumerateObject())
                {
                    parameters.Add(new ToolParameter
                    {
                        Name = prop.Name,
                        Type = ExtractType(prop.Value),
                        Description = ExtractDescription(prop.Value),
                        Required = requiredSet.Contains(prop.Name)
                    });
                }
            }
        }

        return new ToolDefinition
        {
            Name = $"mcp__{serverName}__{mcpTool.Name}",
            Description = mcpTool.Description ?? $"MCP tool '{mcpTool.Name}' from server '{serverName}'.",
            Parameters = parameters
        };
    }

    /// <summary>
    /// Converts a Spectra arguments dictionary into a JSON string suitable
    /// for the MCP <c>tools/call</c> request arguments field.
    /// </summary>
    public static string SerializeArguments(Dictionary<string, object?> arguments)
    {
        return JsonSerializer.Serialize(arguments, JsonOptions.Default);
    }

    /// <summary>
    /// Parses a JSON-RPC result element into a content string.
    /// MCP tool results follow the pattern: { content: [{ type: "text", text: "..." }] }
    /// </summary>
    public static (string Content, bool IsError) ParseToolResult(JsonElement result)
    {
        var isError = false;
        if (result.TryGetProperty("isError", out var errProp))
            isError = errProp.GetBoolean();

        if (result.TryGetProperty("content", out var contentArray)
            && contentArray.ValueKind == JsonValueKind.Array)
        {
            var texts = new List<string>();
            foreach (var item in contentArray.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var textProp))
                    texts.Add(textProp.GetString() ?? string.Empty);
            }

            return (string.Join("\n", texts), isError);
        }

        // Fallback: stringify the whole result
        return (result.GetRawText(), isError);
    }

    private static string ExtractType(JsonElement propertySchema)
    {
        if (propertySchema.TryGetProperty("type", out var typeProp))
            return typeProp.GetString() ?? "string";
        return "string";
    }

    private static string ExtractDescription(JsonElement propertySchema)
    {
        if (propertySchema.TryGetProperty("description", out var descProp))
            return descProp.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }
}