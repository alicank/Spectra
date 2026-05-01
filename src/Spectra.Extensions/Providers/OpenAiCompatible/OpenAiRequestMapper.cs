using System.Text.Json;
using System.Text.Json.Nodes;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Tools;
using static Spectra.Contracts.Providers.LlmMessage;

namespace Spectra.Extensions.Providers.OpenAiCompatible;

internal static class OpenAiRequestMapper
{
    internal static JsonObject Map(LlmRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = MapMessages(request)
        };

        if (request.Temperature.HasValue && !IsReasoningModel(request.Model))
            body["temperature"] = request.Temperature.Value;

        if (request.MaxTokens.HasValue)
        {
            var key = IsReasoningModel(request.Model)
                ? "max_completion_tokens"
                : "max_tokens";
            body[key] = request.MaxTokens.Value;
        }

        if (request.StopSequence is not null)
            body["stop"] = new JsonArray(JsonValue.Create(request.StopSequence));

        MapOutputMode(body, request);
        MapTools(body, request);

        return body;
    }

    private static JsonArray MapMessages(LlmRequest request)
    {
        var messages = new JsonArray();

        if (request.SystemPrompt is not null)
        {
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = request.SystemPrompt
            });
        }

        foreach (var msg in request.Messages)
        {
            messages.Add(MapMessage(msg));
        }

        return messages;
    }

    private static JsonObject MapMessage(LlmMessage msg)
    {
        var obj = new JsonObject
        {
            ["role"] = MapRole(msg.Role)
        };

        if (msg.ToolCallId is not null)
        {
            obj["tool_call_id"] = msg.ToolCallId;
            obj["content"] = msg.Content ?? string.Empty;
            return obj;
        }

        if (msg.Name is not null)
            obj["name"] = msg.Name;

        if (msg.HasMedia && msg.ContentParts is not null)
        {
            obj["content"] = MapContentParts(msg.ContentParts);
        }
        else
        {
            obj["content"] = msg.Content ?? string.Empty;
        }

        if (msg.ToolCalls is { Count: > 0 })
        {
            var toolCalls = new JsonArray();
            foreach (var tc in msg.ToolCalls)
            {
                toolCalls.Add(new JsonObject
                {
                    ["id"] = tc.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tc.Name,
                        ["arguments"] = JsonSerializer.Serialize(tc.Arguments)
                    }
                });
            }
            obj["tool_calls"] = toolCalls;
        }

        return obj;
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
                        ["type"] = "image_url",
                        ["image_url"] = new JsonObject
                        {
                            ["url"] = part.SourceUri
                        }
                    });
                    break;

                case MediaType.Image:
                    arr.Add(new JsonObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JsonObject
                        {
                            ["url"] = $"data:{part.MimeType ?? "image/png"};base64,{part.Data}"
                        }
                    });
                    break;

                default:
                    // Audio / Video — pass as base64 data URI; support varies by provider
                    arr.Add(new JsonObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JsonObject
                        {
                            ["url"] = $"data:{part.MimeType};base64,{part.Data}"
                        }
                    });
                    break;
            }
        }

        return arr;
    }

    private static void MapOutputMode(JsonObject body, LlmRequest request)
    {
        switch (request.OutputMode)
        {
            case LlmOutputMode.Json:
                body["response_format"] = new JsonObject { ["type"] = "json_object" };
                break;

            case LlmOutputMode.StructuredJson when request.JsonSchema is not null:
                body["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject
                    {
                        ["name"] = "structured_output",
                        ["schema"] = JsonNode.Parse(request.JsonSchema)
                    }
                };
                break;
        }
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
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = properties,
                        ["required"] = required
                    }
                }
            });
        }

        body["tools"] = tools;
    }
    
    private static bool IsReasoningModel (string? model)
    {
        if (string.IsNullOrEmpty(model))
            return false;

        // GPT-5 family (gpt-5, gpt-5-mini, gpt-5-nano, gpt-5.1, gpt-5.2, gpt-5.4, etc.)
        if (model.Contains("gpt-5", StringComparison.OrdinalIgnoreCase))
            return true;

        // o-series reasoning models (o1, o1-mini, o3, o3-mini, o4-mini, etc.)
        if (model.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("o4", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string MapRole(LlmRole role) => role switch
    {
        LlmRole.System => "system",
        LlmRole.User => "user",
        LlmRole.Assistant => "assistant",
        LlmRole.Tool => "tool",
        _ => "user"
    };
}