using System.Text.Json;
using System.Text.Json.Nodes;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Tools;
using Spectra.Extensions.Providers.Anthropic;
using Xunit;

namespace Spectra.Tests.Providers;

public class AnthropicRequestMapperTests
{
    private static LlmRequest MinimalRequest(string model = "claude-sonnet-4-20250514") => new()
    {
        Model = model,
        Messages = [LlmMessage.FromText(LlmRole.User, "Hello")]
    };

    // ─── basic mapping ──────────────────────────────────────────────

    [Fact]
    public void Maps_Model()
    {
        var body = AnthropicRequestMapper.Map(MinimalRequest("claude-haiku-3"));
        Assert.Equal("claude-haiku-3", body["model"]!.GetValue<string>());
    }

    [Fact]
    public void Maps_MaxTokens_DefaultsTo4096()
    {
        var body = AnthropicRequestMapper.Map(MinimalRequest());
        Assert.Equal(4096, body["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public void Maps_MaxTokens_FromRequest()
    {
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            MaxTokens = 1024
        };

        var body = AnthropicRequestMapper.Map(request);
        Assert.Equal(1024, body["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public void Maps_SingleUserMessage()
    {
        var body = AnthropicRequestMapper.Map(MinimalRequest());
        var messages = body["messages"]!.AsArray();

        Assert.Single(messages);
        Assert.Equal("user", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("Hello", messages[0]!["content"]!.GetValue<string>());
    }

    // ─── system prompt ──────────────────────────────────────────────

    [Fact]
    public void Maps_SystemPrompt_AsTopLevelField()
    {
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            SystemPrompt = "You are a helpful assistant."
        };

        var body = AnthropicRequestMapper.Map(request);

        Assert.Equal("You are a helpful assistant.", body["system"]!.GetValue<string>());

        // System should NOT appear in messages array
        var messages = body["messages"]!.AsArray();
        Assert.Single(messages);
        Assert.Equal("user", messages[0]!["role"]!.GetValue<string>());
    }

    [Fact]
    public void SystemRoleMessages_MergedIntoSystemField()
    {
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
            Messages =
            [
                LlmMessage.FromText(LlmRole.System, "Instruction A"),
                LlmMessage.FromText(LlmRole.User, "Hi")
            ],
            SystemPrompt = "Base system prompt"
        };

        var body = AnthropicRequestMapper.Map(request);

        var system = body["system"]!.GetValue<string>();
        Assert.Contains("Base system prompt", system);
        Assert.Contains("Instruction A", system);

        // System messages should be filtered from messages array
        var messages = body["messages"]!.AsArray();
        Assert.Single(messages);
        Assert.Equal("user", messages[0]!["role"]!.GetValue<string>());
    }

    [Fact]
    public void OmitsSystemField_WhenNoSystemPrompt()
    {
        var body = AnthropicRequestMapper.Map(MinimalRequest());
        Assert.Null(body["system"]);
    }

    // ─── optional parameters ────────────────────────────────────────

    [Fact]
    public void Maps_Temperature()
    {
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            Temperature = 0.7
        };

        var body = AnthropicRequestMapper.Map(request);
        Assert.Equal(0.7, body["temperature"]!.GetValue<double>(), precision: 5);
    }

    [Fact]
    public void Maps_StopSequences()
    {
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            StopSequence = "END"
        };

        var body = AnthropicRequestMapper.Map(request);
        var stop = body["stop_sequences"]!.AsArray();
        Assert.Single(stop);
        Assert.Equal("END", stop[0]!.GetValue<string>());
    }

    [Fact]
    public void OmitsOptionalFields_WhenNull()
    {
        var body = AnthropicRequestMapper.Map(MinimalRequest());

        Assert.Null(body["temperature"]);
        Assert.Null(body["stop_sequences"]);
        Assert.Null(body["tools"]);
        Assert.Null(body["system"]);
    }

    // ─── output mode ────────────────────────────────────────────────

    [Fact]
    public void JsonOutputMode_AppendsSystemInstruction()
    {
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            OutputMode = LlmOutputMode.Json
        };

        var body = AnthropicRequestMapper.Map(request);

        var system = body["system"]!.GetValue<string>();
        Assert.Contains("valid JSON", system);
    }

    [Fact]
    public void StructuredJsonOutputMode_AppendsSchemaInSystemInstruction()
    {
        var schema = """{"type":"object","properties":{"x":{"type":"string"}}}""";
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            OutputMode = LlmOutputMode.StructuredJson,
            JsonSchema = schema
        };

        var body = AnthropicRequestMapper.Map(request);

        var system = body["system"]!.GetValue<string>();
        Assert.Contains("valid JSON", system);
        Assert.Contains(schema, system);
    }

    [Fact]
    public void JsonOutputMode_CombinesWithExistingSystemPrompt()
    {
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            SystemPrompt = "Be concise.",
            OutputMode = LlmOutputMode.Json
        };

        var body = AnthropicRequestMapper.Map(request);

        var system = body["system"]!.GetValue<string>();
        Assert.Contains("Be concise.", system);
        Assert.Contains("valid JSON", system);
    }

    // ─── tools ──────────────────────────────────────────────────────

    [Fact]
    public void Maps_ToolDefinitions()
    {
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
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

        var body = AnthropicRequestMapper.Map(request);
        var tools = body["tools"]!.AsArray();

        Assert.Single(tools);

        var tool = tools[0]!.AsObject();
        Assert.Equal("get_weather", tool["name"]!.GetValue<string>());
        Assert.Equal("Get current weather", tool["description"]!.GetValue<string>());

        // Anthropic uses input_schema, NOT function wrapper
        var schema = tool["input_schema"]!.AsObject();
        Assert.Equal("object", schema["type"]!.GetValue<string>());

        var properties = schema["properties"]!.AsObject();
        Assert.True(properties.ContainsKey("city"));
        Assert.True(properties.ContainsKey("units"));

        var required = schema["required"]!.AsArray();
        Assert.Single(required);
        Assert.Equal("city", required[0]!.GetValue<string>());
    }

    // ─── tool calls on assistant message ────────────────────────────

    [Fact]
    public void Maps_AssistantMessageWithToolCalls()
    {
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
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
                            Id = "toolu_123",
                            Name = "get_weather",
                            Arguments = new() { ["city"] = "Paris" }
                        }
                    ]
                }
            ]
        };

        var body = AnthropicRequestMapper.Map(request);
        var msg = body["messages"]!.AsArray()[0]!.AsObject();

        Assert.Equal("assistant", msg["role"]!.GetValue<string>());

        // Anthropic uses content blocks, not a separate tool_calls field
        var content = msg["content"]!.AsArray();
        Assert.Equal(2, content.Count);

        Assert.Equal("text", content[0]!["type"]!.GetValue<string>());
        Assert.Equal("Let me check the weather.", content[0]!["text"]!.GetValue<string>());

        Assert.Equal("tool_use", content[1]!["type"]!.GetValue<string>());
        Assert.Equal("toolu_123", content[1]!["id"]!.GetValue<string>());
        Assert.Equal("get_weather", content[1]!["name"]!.GetValue<string>());
        Assert.NotNull(content[1]!["input"]);
    }

    [Fact]
    public void Maps_AssistantToolUse_WithoutTextContent()
    {
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
            Messages =
            [
                new LlmMessage
                {
                    Role = LlmRole.Assistant,
                    Content = null,
                    ToolCalls =
                    [
                        new ToolCall
                        {
                            Id = "toolu_456",
                            Name = "search",
                            Arguments = new() { ["query"] = "test" }
                        }
                    ]
                }
            ]
        };

        var body = AnthropicRequestMapper.Map(request);
        var content = body["messages"]!.AsArray()[0]!["content"]!.AsArray();

        // Only tool_use block, no text block
        Assert.Single(content);
        Assert.Equal("tool_use", content[0]!["type"]!.GetValue<string>());
    }

    // ─── tool result messages ───────────────────────────────────────

    [Fact]
    public void Maps_ToolResultMessage()
    {
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
            Messages =
            [
                LlmMessage.ToolResult("toolu_123", "72°F and sunny")
            ]
        };

        var body = AnthropicRequestMapper.Map(request);
        var msg = body["messages"]!.AsArray()[0]!.AsObject();

        // Tool results become user messages in Anthropic
        Assert.Equal("user", msg["role"]!.GetValue<string>());

        var content = msg["content"]!.AsArray();
        Assert.Single(content);
        Assert.Equal("tool_result", content[0]!["type"]!.GetValue<string>());
        Assert.Equal("toolu_123", content[0]!["tool_use_id"]!.GetValue<string>());
        Assert.Equal("72°F and sunny", content[0]!["content"]!.GetValue<string>());
    }

    // ─── multimodal content ─────────────────────────────────────────

    [Fact]
    public void Maps_ImageContentParts_Base64()
    {
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
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

        var body = AnthropicRequestMapper.Map(request);
        var content = body["messages"]!.AsArray()[0]!["content"]!.AsArray();

        Assert.Equal(2, content.Count);

        Assert.Equal("text", content[0]!["type"]!.GetValue<string>());
        Assert.Equal("What is in this image?", content[0]!["text"]!.GetValue<string>());

        Assert.Equal("image", content[1]!["type"]!.GetValue<string>());
        var source = content[1]!["source"]!.AsObject();
        Assert.Equal("base64", source["type"]!.GetValue<string>());
        Assert.Equal("image/jpeg", source["media_type"]!.GetValue<string>());
        Assert.Equal("abc123base64", source["data"]!.GetValue<string>());
    }

    [Fact]
    public void Maps_ImageFromUri()
    {
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
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
                            SourceUri = "https://example.com/photo.jpg"
                        }
                    ]
                }
            ]
        };

        var body = AnthropicRequestMapper.Map(request);
        var content = body["messages"]!.AsArray()[0]!["content"]!.AsArray();

        Assert.Equal("image", content[0]!["type"]!.GetValue<string>());
        var source = content[0]!["source"]!.AsObject();
        Assert.Equal("url", source["type"]!.GetValue<string>());
        Assert.Equal("https://example.com/photo.jpg", source["url"]!.GetValue<string>());
    }

    [Fact]
    public void Skips_UnsupportedMediaTypes()
    {
        var request = new LlmRequest
        {
            Model = "claude-sonnet-4-20250514",
            Messages =
            [
                new LlmMessage
                {
                    Role = LlmRole.User,
                    ContentParts =
                    [
                        LlmMessage.MediaContent.FromText("Describe this"),
                        LlmMessage.MediaContent.FromAudio("audiodata", "audio/wav")
                    ]
                }
            ]
        };

        var body = AnthropicRequestMapper.Map(request);
        var content = body["messages"]!.AsArray()[0]!["content"]!.AsArray();

        // Audio is unsupported — only the text block should remain
        Assert.Single(content);
        Assert.Equal("text", content[0]!["type"]!.GetValue<string>());
    }
}