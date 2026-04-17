using System.Text.Json;
using System.Text.Json.Nodes;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Tools;
using static Spectra.Contracts.Providers.LlmMessage;

namespace Spectra.Extensions.Providers.Ollama;

internal static class OllamaRequestMapper
{
    internal static JsonObject Map(LlmRequest request, OllamaConfig config)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = MapMessages(request)
        };

        // Ollama uses an "options" object for temperature, num_predict, etc.
        var options = new JsonObject();

        if (request.Temperature.HasValue)
            options["temperature"] = request.Temperature.Value;

        if (request.MaxTokens.HasValue)
            options["num_predict"] = request.MaxTokens.Value;

        if (request.StopSequence is not null)
            options["stop"] = new JsonArray(JsonValue.Create(request.StopSequence));

        // Merge user-supplied options from config
        foreach (var kvp in config.Options)
        {
            if (kvp.Value is not null)
                options[kvp.Key] = JsonValue.Create(kvp.Value);
        }

        if (options.Count > 0)
            body["options"] = options;

        if (config.KeepAlive is not null)
            body["keep_alive"] = config.KeepAlive;

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

        obj["content"] = msg.Content ?? string.Empty;

        // Ollama supports images as base64 strings in an "images" array
        if (msg.HasMedia && msg.ContentParts is not null)
        {
            var images = new JsonArray();
            foreach (var part in msg.ContentParts)
            {
                if (part.Type == MediaType.Image && part.Data is not null)
                    images.Add(JsonValue.Create(part.Data));
            }

            if (images.Count > 0)
                obj["images"] = images;
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

    private static void MapOutputMode(JsonObject body, LlmRequest request)
    {
        switch (request.OutputMode)
        {
            case LlmOutputMode.Json:
            case LlmOutputMode.StructuredJson:
                body["format"] = "json";
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

    private static string MapRole(LlmRole role) => role switch
    {
        LlmRole.System => "system",
        LlmRole.User => "user",
        LlmRole.Assistant => "assistant",
        LlmRole.Tool => "tool",
        _ => "user"
    };
}