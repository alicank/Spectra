using System.Text.Json;
using System.Text.Json.Nodes;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Tools;
using static Spectra.Contracts.Providers.LlmMessage;

namespace Spectra.Extensions.Providers.Gemini;

internal static class GeminiRequestMapper
{
    internal static JsonObject Map(LlmRequest request)
    {
        var body = new JsonObject
        {
            ["contents"] = MapContents(request)
        };

        var systemInstruction = BuildSystemInstruction(request);
        if (systemInstruction is not null)
            body["systemInstruction"] = systemInstruction;

        var generationConfig = BuildGenerationConfig(request);
        if (generationConfig.Count > 0)
            body["generationConfig"] = generationConfig;

        MapTools(body, request);

        return body;
    }

    private static JsonObject? BuildSystemInstruction(LlmRequest request)
    {
        var parts = new List<string>();

        if (request.SystemPrompt is not null)
            parts.Add(request.SystemPrompt);

        foreach (var msg in request.Messages)
        {
            if (msg.Role == LlmRole.System && msg.Content is not null)
                parts.Add(msg.Content);
        }

        if (parts.Count == 0)
            return null;

        return new JsonObject
        {
            ["parts"] = new JsonArray
            {
                new JsonObject { ["text"] = string.Join("\n\n", parts) }
            }
        };
    }

    private static JsonObject BuildGenerationConfig(LlmRequest request)
    {
        var config = new JsonObject();

        if (request.Temperature.HasValue)
            config["temperature"] = request.Temperature.Value;

        if (request.MaxTokens.HasValue)
            config["maxOutputTokens"] = request.MaxTokens.Value;

        if (request.StopSequence is not null)
            config["stopSequences"] = new JsonArray(JsonValue.Create(request.StopSequence));

        switch (request.OutputMode)
        {
            case LlmOutputMode.Json:
                config["responseMimeType"] = "application/json";
                break;

            case LlmOutputMode.StructuredJson when request.JsonSchema is not null:
                config["responseMimeType"] = "application/json";
                config["responseSchema"] = JsonNode.Parse(request.JsonSchema);
                break;
        }

        return config;
    }

    private static JsonArray MapContents(LlmRequest request)
    {
        var contents = new JsonArray();

        foreach (var msg in request.Messages)
        {
            if (msg.Role == LlmRole.System)
                continue; // handled in BuildSystemInstruction

            if (msg.Role == LlmRole.Tool)
            {
                contents.Add(MapToolResultMessage(msg));
                continue;
            }

            if (msg.Role == LlmRole.Assistant && msg.ToolCalls is { Count: > 0 })
            {
                contents.Add(MapModelWithFunctionCall(msg));
                continue;
            }

            contents.Add(MapStandardMessage(msg));
        }

        return contents;
    }

    private static JsonObject MapStandardMessage(LlmMessage msg)
    {
        var obj = new JsonObject
        {
            ["role"] = MapRole(msg.Role)
        };

        if (msg.HasMedia && msg.ContentParts is not null)
        {
            obj["parts"] = MapContentParts(msg.ContentParts);
        }
        else
        {
            obj["parts"] = new JsonArray
            {
                new JsonObject { ["text"] = msg.Content ?? string.Empty }
            };
        }

        return obj;
    }

    /// <summary>
    /// Maps an assistant message that includes tool calls to the Gemini format.
    /// Gemini represents tool calls as functionCall parts within a model message.
    /// </summary>
    private static JsonObject MapModelWithFunctionCall(LlmMessage msg)
    {
        var parts = new JsonArray();

        if (!string.IsNullOrEmpty(msg.Content))
        {
            parts.Add(new JsonObject { ["text"] = msg.Content });
        }

        foreach (var tc in msg.ToolCalls!)
        {
            parts.Add(new JsonObject
            {
                ["functionCall"] = new JsonObject
                {
                    ["name"] = tc.Name,
                    ["args"] = JsonSerializer.SerializeToNode(tc.Arguments)
                }
            });
        }

        return new JsonObject
        {
            ["role"] = "model",
            ["parts"] = parts
        };
    }

    /// <summary>
    /// Gemini expects tool results as a user message containing a functionResponse part.
    /// </summary>
    private static JsonObject MapToolResultMessage(LlmMessage msg)
    {
        // Attempt to parse content as JSON for structured response; fall back to text wrapper.
        JsonNode? responseNode;
        try
        {
            responseNode = JsonNode.Parse(msg.Content ?? "{}");
        }
        catch
        {
            responseNode = new JsonObject { ["result"] = msg.Content ?? string.Empty };
        }

        return new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray
            {
                new JsonObject
                {
                    ["functionResponse"] = new JsonObject
                    {
                        ["name"] = msg.Name ?? msg.ToolCallId ?? "unknown",
                        ["response"] = responseNode
                    }
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
                    arr.Add(new JsonObject { ["text"] = part.Text ?? string.Empty });
                    break;

                case MediaType.Image when part.SourceUri is not null:
                    arr.Add(new JsonObject
                    {
                        ["fileData"] = new JsonObject
                        {
                            ["mimeType"] = part.MimeType ?? "image/png",
                            ["fileUri"] = part.SourceUri
                        }
                    });
                    break;

                case MediaType.Image:
                    arr.Add(new JsonObject
                    {
                        ["inlineData"] = new JsonObject
                        {
                            ["mimeType"] = part.MimeType ?? "image/png",
                            ["data"] = part.Data
                        }
                    });
                    break;

                case MediaType.Audio:
                    arr.Add(new JsonObject
                    {
                        ["inlineData"] = new JsonObject
                        {
                            ["mimeType"] = part.MimeType ?? "audio/wav",
                            ["data"] = part.Data
                        }
                    });
                    break;

                case MediaType.Video when part.SourceUri is not null:
                    arr.Add(new JsonObject
                    {
                        ["fileData"] = new JsonObject
                        {
                            ["mimeType"] = part.MimeType ?? "video/mp4",
                            ["fileUri"] = part.SourceUri
                        }
                    });
                    break;

                case MediaType.Video:
                    arr.Add(new JsonObject
                    {
                        ["inlineData"] = new JsonObject
                        {
                            ["mimeType"] = part.MimeType ?? "video/mp4",
                            ["data"] = part.Data
                        }
                    });
                    break;
            }
        }

        return arr;
    }

    private static void MapTools(JsonObject body, LlmRequest request)
    {
        if (request.Tools is not { Count: > 0 })
            return;

        var functionDeclarations = new JsonArray();

        foreach (var tool in request.Tools)
        {
            var properties = new JsonObject();
            var required = new JsonArray();

            foreach (var param in tool.Parameters)
            {
                properties[param.Name] = new JsonObject
                {
                    ["type"] = param.Type.ToUpperInvariant(),
                    ["description"] = param.Description
                };

                if (param.Required)
                    required.Add(JsonValue.Create(param.Name));
            }

            var declaration = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = new JsonObject
                {
                    ["type"] = "OBJECT",
                    ["properties"] = properties,
                    ["required"] = required
                }
            };

            functionDeclarations.Add(declaration);
        }

        body["tools"] = new JsonArray
        {
            new JsonObject
            {
                ["functionDeclarations"] = functionDeclarations
            }
        };
    }

    private static string MapRole(LlmRole role) => role switch
    {
        LlmRole.User => "user",
        LlmRole.Assistant => "model",
        LlmRole.Tool => "user",
        _ => "user"
    };
}