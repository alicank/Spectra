using Spectra.Extensions.Providers.OpenAiCompatible;
using Xunit;

namespace Spectra.Tests.Providers;

public class OpenAiResponseMapperTests
{
    // ─── completion mapping ─────────────────────────────────────────

    [Fact]
    public void MapCompletion_ExtractsContent()
    {
        var json = """
        {
            "id": "chatcmpl-123",
            "model": "gpt-4o",
            "choices": [{
                "index": 0,
                "message": { "role": "assistant", "content": "Hello world" },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15 }
        }
        """;

        var response = OpenAiResponseMapper.MapCompletion(json, TimeSpan.FromMilliseconds(200));

        Assert.True(response.Success);
        Assert.Equal("Hello world", response.Content);
        Assert.Equal("gpt-4o", response.Model);
        Assert.Equal("stop", response.StopReason);
        Assert.Equal(10, response.InputTokens);
        Assert.Equal(5, response.OutputTokens);
        Assert.Equal(TimeSpan.FromMilliseconds(200), response.Latency);
        Assert.False(response.HasToolCalls);
    }

    [Fact]
    public void MapCompletion_HandlesNullContent()
    {
        var json = """
        {
            "model": "gpt-4o",
            "choices": [{
                "index": 0,
                "message": { "role": "assistant", "content": null },
                "finish_reason": "stop"
            }]
        }
        """;

        var response = OpenAiResponseMapper.MapCompletion(json, TimeSpan.Zero);

        Assert.True(response.Success);
        Assert.Equal(string.Empty, response.Content);
    }

    [Fact]
    public void MapCompletion_ExtractsToolCalls()
    {
        var json = """
        {
            "model": "gpt-4o",
            "choices": [{
                "index": 0,
                "message": {
                    "role": "assistant",
                    "content": null,
                    "tool_calls": [
                        {
                            "id": "call_abc",
                            "type": "function",
                            "function": {
                                "name": "get_weather",
                                "arguments": "{\"city\":\"Paris\"}"
                            }
                        }
                    ]
                },
                "finish_reason": "tool_calls"
            }],
            "usage": { "prompt_tokens": 15, "completion_tokens": 20, "total_tokens": 35 }
        }
        """;

        var response = OpenAiResponseMapper.MapCompletion(json, TimeSpan.Zero);

        Assert.True(response.Success);
        Assert.True(response.HasToolCalls);
        Assert.Single(response.ToolCalls!);

        var tc = response.ToolCalls![0];
        Assert.Equal("call_abc", tc.Id);
        Assert.Equal("get_weather", tc.Name);
        Assert.True(tc.Arguments.ContainsKey("city"));
    }

    [Fact]
    public void MapCompletion_MultipleToolCalls()
    {
        var json = """
        {
            "model": "gpt-4o",
            "choices": [{
                "index": 0,
                "message": {
                    "role": "assistant",
                    "content": null,
                    "tool_calls": [
                        {
                            "id": "call_1",
                            "type": "function",
                            "function": { "name": "tool_a", "arguments": "{}" }
                        },
                        {
                            "id": "call_2",
                            "type": "function",
                            "function": { "name": "tool_b", "arguments": "{\"x\":1}" }
                        }
                    ]
                },
                "finish_reason": "tool_calls"
            }]
        }
        """;

        var response = OpenAiResponseMapper.MapCompletion(json, TimeSpan.Zero);

        Assert.Equal(2, response.ToolCalls!.Count);
        Assert.Equal("tool_a", response.ToolCalls[0].Name);
        Assert.Equal("tool_b", response.ToolCalls[1].Name);
    }

    [Fact]
    public void MapCompletion_NoUsage_TokensAreNull()
    {
        var json = """
        {
            "model": "gpt-4o",
            "choices": [{
                "index": 0,
                "message": { "role": "assistant", "content": "hi" },
                "finish_reason": "stop"
            }]
        }
        """;

        var response = OpenAiResponseMapper.MapCompletion(json, TimeSpan.Zero);

        Assert.Null(response.InputTokens);
        Assert.Null(response.OutputTokens);
    }

    // ─── stream delta extraction ────────────────────────────────────

    [Fact]
    public void ExtractStreamDelta_ReturnsContentDelta()
    {
        var chunk = """
        {
            "choices": [{
                "index": 0,
                "delta": { "content": "Hello" },
                "finish_reason": null
            }]
        }
        """;

        var delta = OpenAiResponseMapper.ExtractStreamDelta(chunk);
        Assert.Equal("Hello", delta);
    }

    [Fact]
    public void ExtractStreamDelta_ReturnsNull_ForRoleOnlyChunk()
    {
        var chunk = """
        {
            "choices": [{
                "index": 0,
                "delta": { "role": "assistant" },
                "finish_reason": null
            }]
        }
        """;

        var delta = OpenAiResponseMapper.ExtractStreamDelta(chunk);
        Assert.Null(delta);
    }

    [Fact]
    public void ExtractStreamDelta_ReturnsNull_ForFinishChunk()
    {
        var chunk = """
        {
            "choices": [{
                "index": 0,
                "delta": {},
                "finish_reason": "stop"
            }]
        }
        """;

        var delta = OpenAiResponseMapper.ExtractStreamDelta(chunk);
        Assert.Null(delta);
    }

    [Fact]
    public void ExtractStreamDelta_ReturnsNull_ForEmptyChoices()
    {
        var chunk = """{ "choices": [] }""";
        Assert.Null(OpenAiResponseMapper.ExtractStreamDelta(chunk));
    }

    // ─── FromStreamedContent ────────────────────────────────────────

    [Fact]
    public void FromStreamedContent_BuildsResponse()
    {
        var response = OpenAiResponseMapper.FromStreamedContent(
            "accumulated text", TimeSpan.FromSeconds(1), "gpt-4o");

        Assert.True(response.Success);
        Assert.Equal("accumulated text", response.Content);
        Assert.Equal("gpt-4o", response.Model);
        Assert.Equal("stop", response.StopReason);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Latency);
    }
}