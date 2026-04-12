using System.Text.Json;
using System.Text.Json.Nodes;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Tools;
using Spectra.Extensions.Providers.OpenAiCompatible;
using Xunit;

namespace Spectra.Tests.Providers;

public class OpenAiRequestMapperTests
{
    private static LlmRequest MinimalRequest(string model = "gpt-4o") => new()
    {
        Model = model,
        Messages = [LlmMessage.FromText(LlmRole.User, "Hello")]
    };

    // ─── basic mapping ──────────────────────────────────────────────

    [Fact]
    public void Maps_Model()
    {
        var body = OpenAiRequestMapper.Map(MinimalRequest("gpt-4o-mini"));
        Assert.Equal("gpt-4o-mini", body["model"]!.GetValue<string>());
    }

    [Fact]
    public void Maps_SingleUserMessage()
    {
        var body = OpenAiRequestMapper.Map(MinimalRequest());
        var messages = body["messages"]!.AsArray();

        Assert.Single(messages);
        Assert.Equal("user", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("Hello", messages[0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void Maps_SystemPrompt_AsSeparateMessage()
    {
        var request = new LlmRequest
        {
            Model = "gpt-4o",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            SystemPrompt = "You are a helpful assistant."
        };

        var body = OpenAiRequestMapper.Map(request);
        var messages = body["messages"]!.AsArray();

        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("You are a helpful assistant.", messages[0]!["content"]!.GetValue<string>());
        Assert.Equal("user", messages[1]!["role"]!.GetValue<string>());
    }

    [Fact]
    public void Maps_AllRoles()
    {
        var request = new LlmRequest
        {
            Model = "gpt-4o",
            Messages =
            [
                LlmMessage.FromText(LlmRole.User, "question"),
                LlmMessage.FromText(LlmRole.Assistant, "answer"),
                LlmMessage.ToolResult("call-1", "result")
            ]
        };

        var body = OpenAiRequestMapper.Map(request);
        var messages = body["messages"]!.AsArray();

        Assert.Equal("user", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("assistant", messages[1]!["role"]!.GetValue<string>());
        Assert.Equal("tool", messages[2]!["role"]!.GetValue<string>());
        Assert.Equal("call-1", messages[2]!["tool_call_id"]!.GetValue<string>());
    }

    // ─── optional parameters ────────────────────────────────────────

    [Fact]
    public void Maps_Temperature()
    {
        var request = MinimalRequest();
        request = new LlmRequest
        {
            Model = request.Model,
            Messages = request.Messages,
            Temperature = 0.3
        };

        var body = OpenAiRequestMapper.Map(request);
        Assert.Equal(0.3, body["temperature"]!.GetValue<double>(), precision: 5);
    }

    [Fact]
    public void Maps_MaxTokens()
    {
        var request = new LlmRequest
        {
            Model = "gpt-4o",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            MaxTokens = 500
        };

        var body = OpenAiRequestMapper.Map(request);
        Assert.Equal(500, body["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public void Maps_StopSequence()
    {
        var request = new LlmRequest
        {
            Model = "gpt-4o",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            StopSequence = "END"
        };

        var body = OpenAiRequestMapper.Map(request);
        var stop = body["stop"]!.AsArray();
        Assert.Single(stop);
        Assert.Equal("END", stop[0]!.GetValue<string>());
    }

    [Fact]
    public void OmitsOptionalFields_WhenNull()
    {
        var body = OpenAiRequestMapper.Map(MinimalRequest());

        Assert.Null(body["temperature"]);
        Assert.Null(body["max_tokens"]);
        Assert.Null(body["stop"]);
        Assert.Null(body["tools"]);
        Assert.Null(body["response_format"]);
    }

    // ─── output mode ────────────────────────────────────────────────

    [Fact]
    public void Maps_JsonOutputMode()
    {
        var request = new LlmRequest
        {
            Model = "gpt-4o",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            OutputMode = LlmOutputMode.Json
        };

        var body = OpenAiRequestMapper.Map(request);
        var format = body["response_format"]!.AsObject();
        Assert.Equal("json_object", format["type"]!.GetValue<string>());
    }

    [Fact]
    public void Maps_StructuredJsonOutputMode()
    {
        var schema = """{"name":"test","strict":true,"schema":{"type":"object","properties":{"x":{"type":"string"}}}}""";
        var request = new LlmRequest
        {
            Model = "gpt-4o",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            OutputMode = LlmOutputMode.StructuredJson,
            JsonSchema = schema
        };

        var body = OpenAiRequestMapper.Map(request);
        var format = body["response_format"]!.AsObject();
        Assert.Equal("json_schema", format["type"]!.GetValue<string>());
        Assert.NotNull(format["json_schema"]);
    }

    // ─── tools ──────────────────────────────────────────────────────

    [Fact]
    public void Maps_ToolDefinitions()
    {
        var request = new LlmRequest
        {
            Model = "gpt-4o",
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

        var body = OpenAiRequestMapper.Map(request);
        var tools = body["tools"]!.AsArray();

        Assert.Single(tools);

        var tool = tools[0]!.AsObject();
        Assert.Equal("function", tool["type"]!.GetValue<string>());

        var function = tool["function"]!.AsObject();
        Assert.Equal("get_weather", function["name"]!.GetValue<string>());
        Assert.Equal("Get current weather", function["description"]!.GetValue<string>());

        var parameters = function["parameters"]!.AsObject();
        Assert.Equal("object", parameters["type"]!.GetValue<string>());

        var properties = parameters["properties"]!.AsObject();
        Assert.True(properties.ContainsKey("city"));
        Assert.True(properties.ContainsKey("units"));

        var required = parameters["required"]!.AsArray();
        Assert.Single(required);
        Assert.Equal("city", required[0]!.GetValue<string>());
    }

    // ─── tool calls on assistant message ────────────────────────────

    [Fact]
    public void Maps_AssistantMessageWithToolCalls()
    {
        var request = new LlmRequest
        {
            Model = "gpt-4o",
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
                            Id = "call_123",
                            Name = "get_weather",
                            Arguments = new() { ["city"] = "Paris" }
                        }
                    ]
                }
            ]
        };

        var body = OpenAiRequestMapper.Map(request);
        var msg = body["messages"]!.AsArray()[0]!.AsObject();

        var toolCalls = msg["tool_calls"]!.AsArray();
        Assert.Single(toolCalls);
        Assert.Equal("call_123", toolCalls[0]!["id"]!.GetValue<string>());
        Assert.Equal("function", toolCalls[0]!["type"]!.GetValue<string>());

        var fn = toolCalls[0]!["function"]!.AsObject();
        Assert.Equal("get_weather", fn["name"]!.GetValue<string>());

        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            fn["arguments"]!.GetValue<string>());
        Assert.NotNull(args);
        Assert.True(args.ContainsKey("city"));
    }

    // ─── multimodal content ─────────────────────────────────────────

    [Fact]
    public void Maps_ImageContentParts()
    {
        var request = new LlmRequest
        {
            Model = "gpt-4o",
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

        var body = OpenAiRequestMapper.Map(request);
        var content = body["messages"]!.AsArray()[0]!["content"]!.AsArray();

        Assert.Equal(2, content.Count);
        Assert.Equal("text", content[0]!["type"]!.GetValue<string>());
        Assert.Equal("What is in this image?", content[0]!["text"]!.GetValue<string>());
        Assert.Equal("image_url", content[1]!["type"]!.GetValue<string>());

        var url = content[1]!["image_url"]!["url"]!.GetValue<string>();
        Assert.StartsWith("data:image/jpeg;base64,", url);
        Assert.Contains("abc123base64", url);
    }

    [Fact]
    public void Maps_ImageFromUri()
    {
        var request = new LlmRequest
        {
            Model = "gpt-4o",
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

        var body = OpenAiRequestMapper.Map(request);
        var url = body["messages"]!.AsArray()[0]!["content"]!
            .AsArray()[0]!["image_url"]!["url"]!.GetValue<string>();

        Assert.Equal("https://example.com/photo.jpg", url);
    }
}