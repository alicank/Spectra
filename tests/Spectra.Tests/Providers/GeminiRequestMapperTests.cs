using System.Text.Json;
using System.Text.Json.Nodes;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Tools;
using Spectra.Extensions.Providers.Gemini;
using Xunit;

namespace Spectra.Tests.Providers;

public class GeminiRequestMapperTests
{
    private static LlmRequest MinimalRequest(string model = "gemini-2.0-flash") => new()
    {
        Model = model,
        Messages = [LlmMessage.FromText(LlmRole.User, "Hello")]
    };

    // ─── basic mapping ──────────────────────────────────────────────

    [Fact]
    public void Maps_SingleUserMessage_AsContents()
    {
        var body = GeminiRequestMapper.Map(MinimalRequest());
        var contents = body["contents"]!.AsArray();

        Assert.Single(contents);
        Assert.Equal("user", contents[0]!["role"]!.GetValue<string>());
        var parts = contents[0]!["parts"]!.AsArray();
        Assert.Single(parts);
        Assert.Equal("Hello", parts[0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void OmitsSystemInstruction_WhenNoSystemPrompt()
    {
        var body = GeminiRequestMapper.Map(MinimalRequest());
        Assert.Null(body["systemInstruction"]);
    }

    // ─── system prompt ──────────────────────────────────────────────

    [Fact]
    public void Maps_SystemPrompt_AsSystemInstruction()
    {
        var request = new LlmRequest
        {
            Model = "gemini-2.0-flash",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            SystemPrompt = "You are a helpful assistant."
        };

        var body = GeminiRequestMapper.Map(request);

        var si = body["systemInstruction"]!.AsObject();
        var text = si["parts"]![0]!["text"]!.GetValue<string>();
        Assert.Equal("You are a helpful assistant.", text);

        // System should NOT appear in contents array
        var contents = body["contents"]!.AsArray();
        Assert.Single(contents);
        Assert.Equal("user", contents[0]!["role"]!.GetValue<string>());
    }

    [Fact]
    public void SystemRoleMessages_MergedIntoSystemInstruction()
    {
        var request = new LlmRequest
        {
            Model = "gemini-2.0-flash",
            Messages =
            [
                LlmMessage.FromText(LlmRole.System, "Instruction A"),
                LlmMessage.FromText(LlmRole.User, "Hi")
            ],
            SystemPrompt = "Base system prompt"
        };

        var body = GeminiRequestMapper.Map(request);

        var text = body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Base system prompt", text);
        Assert.Contains("Instruction A", text);

        var contents = body["contents"]!.AsArray();
        Assert.Single(contents);
        Assert.Equal("user", contents[0]!["role"]!.GetValue<string>());
    }

    // ─── generation config ──────────────────────────────────────────

    [Fact]
    public void Maps_Temperature()
    {
        var request = new LlmRequest
        {
            Model = "gemini-2.0-flash",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            Temperature = 0.7
        };

        var body = GeminiRequestMapper.Map(request);
        var config = body["generationConfig"]!.AsObject();
        Assert.Equal(0.7, config["temperature"]!.GetValue<double>(), precision: 5);
    }

    [Fact]
    public void Maps_MaxTokens_AsMaxOutputTokens()
    {
        var request = new LlmRequest
        {
            Model = "gemini-2.0-flash",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            MaxTokens = 1024
        };

        var body = GeminiRequestMapper.Map(request);
        var config = body["generationConfig"]!.AsObject();
        Assert.Equal(1024, config["maxOutputTokens"]!.GetValue<int>());
    }

    [Fact]
    public void Maps_StopSequences()
    {
        var request = new LlmRequest
        {
            Model = "gemini-2.0-flash",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            StopSequence = "END"
        };

        var body = GeminiRequestMapper.Map(request);
        var config = body["generationConfig"]!.AsObject();
        var stop = config["stopSequences"]!.AsArray();
        Assert.Single(stop);
        Assert.Equal("END", stop[0]!.GetValue<string>());
    }

    [Fact]
    public void OmitsGenerationConfig_WhenNoOptionsSet()
    {
        var body = GeminiRequestMapper.Map(MinimalRequest());
        // generationConfig should be present but empty, or absent
        if (body.ContainsKey("generationConfig"))
        {
            var config = body["generationConfig"]!.AsObject();
            Assert.Empty(config);
        }
    }

    // ─── output mode ────────────────────────────────────────────────

    [Fact]
    public void JsonOutputMode_SetsResponseMimeType()
    {
        var request = new LlmRequest
        {
            Model = "gemini-2.0-flash",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            OutputMode = LlmOutputMode.Json
        };

        var body = GeminiRequestMapper.Map(request);
        var config = body["generationConfig"]!.AsObject();
        Assert.Equal("application/json", config["responseMimeType"]!.GetValue<string>());
    }

    [Fact]
    public void StructuredJsonOutputMode_SetsSchemaAndMimeType()
    {
        var schema = """{"type":"object","properties":{"x":{"type":"string"}}}""";
        var request = new LlmRequest
        {
            Model = "gemini-2.0-flash",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            OutputMode = LlmOutputMode.StructuredJson,
            JsonSchema = schema
        };

        var body = GeminiRequestMapper.Map(request);
        var config = body["generationConfig"]!.AsObject();
        Assert.Equal("application/json", config["responseMimeType"]!.GetValue<string>());
        Assert.NotNull(config["responseSchema"]);
    }

    // ─── tools ──────────────────────────────────────────────────────

    [Fact]
    public void Maps_ToolDefinitions_AsFunctionDeclarations()
    {
        var request = new LlmRequest
        {
            Model = "gemini-2.0-flash",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            Tools =
            [
                new ToolDefinition
                {
                    Name = "get_weather",
                    Description = "Get current weather",
                    Parameters =
                    [
                        new ToolParameter
                        {
                            Name = "city",
                            Type = "string",
                            Description = "City name",
                            Required = true
                        },
                        new ToolParameter
                        {
                            Name = "units",
                            Type = "string",
                            Description = "Temperature units",
                            Required = false
                        }
                    ]
                }
            ]
        };

        var body = GeminiRequestMapper.Map(request);
        var tools = body["tools"]!.AsArray();
        Assert.Single(tools);

        var declarations = tools[0]!["functionDeclarations"]!.AsArray();
        Assert.Single(declarations);

        var decl = declarations[0]!.AsObject();
        Assert.Equal("get_weather", decl["name"]!.GetValue<string>());
        Assert.Equal("Get current weather", decl["description"]!.GetValue<string>());

        var parameters = decl["parameters"]!.AsObject();
        Assert.Equal("OBJECT", parameters["type"]!.GetValue<string>());

        var properties = parameters["properties"]!.AsObject();
        Assert.True(properties.ContainsKey("city"));
        Assert.True(properties.ContainsKey("units"));

        // Gemini uses uppercase type names
        Assert.Equal("STRING", properties["city"]!["type"]!.GetValue<string>());

        var required = parameters["required"]!.AsArray();
        Assert.Single(required);
        Assert.Equal("city", required[0]!.GetValue<string>());
    }

    [Fact]
    public void OmitsTools_WhenNull()
    {
        var body = GeminiRequestMapper.Map(MinimalRequest());
        Assert.Null(body["tools"]);
    }

    // ─── tool calls on assistant message ────────────────────────────

    [Fact]
    public void Maps_AssistantMessageWithToolCalls_AsFunctionCall()
    {
        var request = new LlmRequest
        {
            Model = "gemini-2.0-flash",
            Messages =
            [
                new LlmMessage
                {
                    Role = LlmRole.Assistant,
                    Content = "Let me check the weather.",
                    ToolCalls =
                    [
                        new ToolCall
                        {
                            Id = "call_123",
                            Name = "get_weather",
                            Arguments = new() { ["city"] = "Paris" }
                        }
                    ]
                }
            ]
        };

        var body = GeminiRequestMapper.Map(request);
        var msg = body["contents"]!.AsArray()[0]!.AsObject();

        Assert.Equal("model", msg["role"]!.GetValue<string>());

        var parts = msg["parts"]!.AsArray();
        Assert.Equal(2, parts.Count);

        Assert.Equal("Let me check the weather.", parts[0]!["text"]!.GetValue<string>());

        var fc = parts[1]!["functionCall"]!.AsObject();
        Assert.Equal("get_weather", fc["name"]!.GetValue<string>());
        Assert.NotNull(fc["args"]);
    }

    // ─── tool result messages ───────────────────────────────────────

    [Fact]
    public void Maps_ToolResultMessage_AsFunctionResponse()
    {
        var request = new LlmRequest
        {
            Model = "gemini-2.0-flash",
            Messages =
            [
                new LlmMessage
                {
                    Role = LlmRole.Tool,
                    ToolCallId = "call_123",
                    Name = "get_weather",
                    Content = """{"temp":"72F","condition":"sunny"}"""
                }
            ]
        };

        var body = GeminiRequestMapper.Map(request);
        var msg = body["contents"]!.AsArray()[0]!.AsObject();

        Assert.Equal("user", msg["role"]!.GetValue<string>());

        var fr = msg["parts"]![0]!["functionResponse"]!.AsObject();
        Assert.Equal("get_weather", fr["name"]!.GetValue<string>());
        Assert.NotNull(fr["response"]);
    }

    // ─── multimodal content ─────────────────────────────────────────

    [Fact]
    public void Maps_ImageContentParts_Base64()
    {
        var request = new LlmRequest
        {
            Model = "gemini-2.0-flash",
            Messages =
            [
                new LlmMessage
                {
                    Role = LlmRole.User,
                    ContentParts =
                    [
                        LlmMessage.MediaContent.FromText("What is in this image?"),
                        LlmMessage.MediaContent.FromImage("abc123base64", "image/jpeg")
                    ]
                }
            ]
        };

        var body = GeminiRequestMapper.Map(request);
        var parts = body["contents"]![0]!["parts"]!.AsArray();

        Assert.Equal(2, parts.Count);

        Assert.Equal("What is in this image?", parts[0]!["text"]!.GetValue<string>());

        var inlineData = parts[1]!["inlineData"]!.AsObject();
        Assert.Equal("image/jpeg", inlineData["mimeType"]!.GetValue<string>());
        Assert.Equal("abc123base64", inlineData["data"]!.GetValue<string>());
    }

    [Fact]
    public void Maps_ImageFromUri_AsFileData()
    {
        var request = new LlmRequest
        {
            Model = "gemini-2.0-flash",
            Messages =
            [
                new LlmMessage
                {
                    Role = LlmRole.User,
                    ContentParts =
                    [
                        new LlmMessage.MediaContent
                        {
                            Type = LlmMessage.MediaType.Image,
                            SourceUri = "https://example.com/photo.jpg",
                            MimeType = "image/jpeg"
                        }
                    ]
                }
            ]
        };

        var body = GeminiRequestMapper.Map(request);
        var parts = body["contents"]![0]!["parts"]!.AsArray();

        var fileData = parts[0]!["fileData"]!.AsObject();
        Assert.Equal("image/jpeg", fileData["mimeType"]!.GetValue<string>());
        Assert.Equal("https://example.com/photo.jpg", fileData["fileUri"]!.GetValue<string>());
    }

    [Fact]
    public void Maps_AudioContent_AsInlineData()
    {
        var request = new LlmRequest
        {
            Model = "gemini-2.0-flash",
            Messages =
            [
                new LlmMessage
                {
                    Role = LlmRole.User,
                    ContentParts =
                    [
                        LlmMessage.MediaContent.FromText("Transcribe this"),
                        LlmMessage.MediaContent.FromAudio("audiodata", "audio/wav")
                    ]
                }
            ]
        };

        var body = GeminiRequestMapper.Map(request);
        var parts = body["contents"]![0]!["parts"]!.AsArray();

        Assert.Equal(2, parts.Count);
        var inlineData = parts[1]!["inlineData"]!.AsObject();
        Assert.Equal("audio/wav", inlineData["mimeType"]!.GetValue<string>());
        Assert.Equal("audiodata", inlineData["data"]!.GetValue<string>());
    }
}