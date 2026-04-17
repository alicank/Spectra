using System.Text.Json;
using System.Text.Json.Nodes;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Tools;
using static Spectra.Contracts.Providers.LlmMessage;

namespace Spectra.Extensions.Providers.Anthropic;

internal static class AnthropicRequestMapper
{
    private const int DefaultMaxTokens = 4096;

    internal static JsonObject Map(LlmRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["max_tokens"] = request.MaxTokens ?? DefaultMaxTokens,
            ["messages"] = MapMessages(request)
        };

        var system = BuildSystemPrompt(request);
        if (system is not null)
            body["system"] = system;

        if (request.Temperature.HasValue)
            body["temperature"] = request.Temperature.Value;

        if (request.StopSequence is not null)
            body["stop_sequences"] = new JsonArray(JsonValue.Create(request.StopSequence));

        MapTools(body, request);

        return body;
    }

    /// <summary>
    /// Anthropic has no native json_mode. We append an instruction to the system prompt
    /// and, for StructuredJson, include the schema in the instruction.
    /// </summary>
    private static string? BuildSystemPrompt(LlmRequest request)
    {
        var parts = new List<string>();

        if (request.SystemPrompt is not null)
            parts.Add(request.SystemPrompt);

        // Collect any system-role messages and merge them in
        foreach (var msg in request.Messages)
        {
            if (msg.Role == LlmRole.System && msg.Content is not null)
                parts.Add(msg.Content);
        }

        switch (request.OutputMode)
        {
            case LlmOutputMode.Json:
                parts.Add("Respond with valid JSON only. Do not include any text outside the JSON object.");
                break;

            case LlmOutputMode.StructuredJson when request.JsonSchema is not null:
                parts.Add($"Respond with valid JSON matching this schema:\n{request.JsonSchema}\nDo not include any text outside the JSON object.");
                break;
        }

        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }

    private static JsonArray MapMessages(LlmRequest request)
    {
        var messages = new JsonArray();

        foreach (var msg in request.Messages)
        {
            if (msg.Role == LlmRole.System)
                continue; // handled in BuildSystemPrompt

            if (msg.Role == LlmRole.Tool)
            {
                messages.Add(MapToolResultMessage(msg));
                continue;
            }

            if (msg.Role == LlmRole.Assistant && msg.ToolCalls is { Count: > 0 })
            {
                messages.Add(MapAssistantWithToolUse(msg));
                continue;
            }

            messages.Add(MapStandardMessage(msg));
        }

        return messages;
    }

    private static JsonObject MapStandardMessage(LlmMessage msg)
    {
        var obj = new JsonObject
        {
            ["role"] = MapRole(msg.Role)
        };

        if (msg.HasMedia && msg.ContentParts is not null)
        {
            obj["content"] = MapContentParts(msg.ContentParts);
        }
        else
        {
            obj["content"] = msg.Content ?? string.Empty;
        }

        return obj;
    }

    /// <summary>
    /// Maps an assistant message that includes tool calls to the Anthropic format.
    /// Anthropic represents tool calls as content blocks of type "tool_use" within
    /// the assistant message, alongside any text content.
    /// </summary>
    private static JsonObject MapAssistantWithToolUse(LlmMessage msg)
    {
        var content = new JsonArray();

        if (!string.IsNullOrEmpty(msg.Content))
        {
            content.Add(new JsonObject
            {
                ["type"] = "text",
                ["text"] = msg.Content
            });
        }

        foreach (var tc in msg.ToolCalls!)
        {
            content.Add(new JsonObject
            {
                ["type"] = "tool_use",
                ["id"] = tc.Id,
                ["name"] = tc.Name,
                ["input"] = JsonSerializer.SerializeToNode(tc.Arguments)
            });
        }

        return new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = content
        };
    }

    /// <summary>
    /// Anthropic expects tool results as a user message containing a tool_result content block.
    /// </summary>
    private static JsonObject MapToolResultMessage(LlmMessage msg)
    {
        return new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = msg.ToolCallId,
                    ["content"] = msg.Content ?? string.Empty
                }
            }
        };
    }

    private static JsonArray MapContentParts(List<MediaContent> parts)
    {
        var arr = new JsonArray();

        foreach (var part in parts)
        {
            switch (part.Type)
            {
                case MediaType.Text:
                    arr.Add(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = part.Text ?? string.Empty
                    });
                    break;

                case MediaType.Image when part.SourceUri is not null:
                    arr.Add(new JsonObject
                    {
                        ["type"] = "image",
                        ["source"] = new JsonObject
                        {
                            ["type"] = "url",
                            ["url"] = part.SourceUri
                        }
                    });
                    break;

                case MediaType.Image:
                    arr.Add(new JsonObject
                    {
                        ["type"] = "image",
                        ["source"] = new JsonObject
                        {
                            ["type"] = "base64",
                            ["media_type"] = part.MimeType ?? "image/png",
                            ["data"] = part.Data
                        }
                    });
                    break;

                default:
                    // Audio / Video — Anthropic does not natively support these;
                    // skip unsupported media types silently.
                    break;
            }
        }

        return arr;
    }

    private static void MapTools(JsonObject body, LlmRequest request)
    {
        if (request.Tools is not { Count: > 0 })
            return;

        var tools = new JsonArray();

        foreach (var tool in request.Tools)
        {
            var properties = new JsonObject();
            var required = new JsonArray();

            foreach (var param in tool.Parameters)
            {
                if (param.RawSchema is not null)
                {
                    properties[param.Name] = JsonNode.Parse(param.RawSchema);
                }
                else
                {
                    properties[param.Name] = new JsonObject
                    {
                        ["type"] = param.Type,
                        ["description"] = param.Description
                    };
                }

                if (param.Required)
                    required.Add(JsonValue.Create(param.Name));
            }

            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = required
                }
            });
        }

        body["tools"] = tools;
    }

    private static string MapRole(LlmRole role) => role switch
    {
        LlmRole.User => "user",
        LlmRole.Assistant => "assistant",
        _ => "user"
    };
}