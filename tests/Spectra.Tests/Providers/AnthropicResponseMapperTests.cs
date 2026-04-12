using Spectra.Extensions.Providers.Anthropic;
using Xunit;

namespace Spectra.Tests.Providers;

public class AnthropicResponseMapperTests
{
    // ─── completion mapping ─────────────────────────────────────────

    [Fact]
    public void MapCompletion_ExtractsContent()
    {
        var json = """
        {
            "id": "msg_test123",
            "type": "message",
            "role": "assistant",
            "model": "claude-sonnet-4-20250514",
            "content": [
                { "type": "text", "text": "Hello world" }
            ],
            "stop_reason": "end_turn",
            "usage": { "input_tokens": 10, "output_tokens": 5 }
        }
        """;

        var response = AnthropicResponseMapper.MapCompletion(json, TimeSpan.FromMilliseconds(200));

        Assert.True(response.Success);
        Assert.Equal("Hello world", response.Content);
        Assert.Equal("claude-sonnet-4-20250514", response.Model);
        Assert.Equal("end_turn", response.StopReason);
        Assert.Equal(10, response.InputTokens);
        Assert.Equal(5, response.OutputTokens);
        Assert.Equal(TimeSpan.FromMilliseconds(200), response.Latency);
        Assert.False(response.HasToolCalls);
    }

    [Fact]
    public void MapCompletion_HandlesEmptyContent()
    {
        var json = """
        {
            "id": "msg_test",
            "type": "message",
            "role": "assistant",
            "model": "claude-sonnet-4-20250514",
            "content": [],
            "stop_reason": "end_turn"
        }
        """;

        var response = AnthropicResponseMapper.MapCompletion(json, TimeSpan.Zero);

        Assert.True(response.Success);
        Assert.Equal(string.Empty, response.Content);
    }

    [Fact]
    public void MapCompletion_ConcatsMultipleTextBlocks()
    {
        var json = """
        {
            "id": "msg_test",
            "type": "message",
            "role": "assistant",
            "model": "claude-sonnet-4-20250514",
            "content": [
                { "type": "text", "text": "First part. " },
                { "type": "text", "text": "Second part." }
            ],
            "stop_reason": "end_turn"
        }
        """;

        var response = AnthropicResponseMapper.MapCompletion(json, TimeSpan.Zero);

        Assert.Equal("First part. Second part.", response.Content);
    }

    [Fact]
    public void MapCompletion_ExtractsToolCalls()
    {
        var json = """
        {
            "id": "msg_test",
            "type": "message",
            "role": "assistant",
            "model": "claude-sonnet-4-20250514",
            "content": [
                {
                    "type": "tool_use",
                    "id": "toolu_abc",
                    "name": "get_weather",
                    "input": { "city": "Paris" }
                }
            ],
            "stop_reason": "tool_use",
            "usage": { "input_tokens": 15, "output_tokens": 20 }
        }
        """;

        var response = AnthropicResponseMapper.MapCompletion(json, TimeSpan.Zero);

        Assert.True(response.Success);
        Assert.True(response.HasToolCalls);
        Assert.Single(response.ToolCalls!);

        var tc = response.ToolCalls![0];
        Assert.Equal("toolu_abc", tc.Id);
        Assert.Equal("get_weather", tc.Name);
        Assert.True(tc.Arguments.ContainsKey("city"));
    }

    [Fact]
    public void MapCompletion_MultipleToolCalls()
    {
        var json = """
        {
            "id": "msg_test",
            "type": "message",
            "role": "assistant",
            "model": "claude-sonnet-4-20250514",
            "content": [
                { "type": "text", "text": "Let me look that up." },
                {
                    "type": "tool_use",
                    "id": "toolu_1",
                    "name": "tool_a",
                    "input": {}
                },
                {
                    "type": "tool_use",
                    "id": "toolu_2",
                    "name": "tool_b",
                    "input": { "x": 1 }
                }
            ],
            "stop_reason": "tool_use"
        }
        """;

        var response = AnthropicResponseMapper.MapCompletion(json, TimeSpan.Zero);

        Assert.Equal("Let me look that up.", response.Content);
        Assert.Equal(2, response.ToolCalls!.Count);
        Assert.Equal("tool_a", response.ToolCalls[0].Name);
        Assert.Equal("tool_b", response.ToolCalls[1].Name);
    }

    [Fact]
    public void MapCompletion_NoUsage_TokensAreNull()
    {
        var json = """
        {
            "id": "msg_test",
            "type": "message",
            "role": "assistant",
            "model": "claude-sonnet-4-20250514",
            "content": [{ "type": "text", "text": "hi" }],
            "stop_reason": "end_turn"
        }
        """;

        var response = AnthropicResponseMapper.MapCompletion(json, TimeSpan.Zero);

        Assert.Null(response.InputTokens);
        Assert.Null(response.OutputTokens);
    }

    // ─── stream delta extraction ────────────────────────────────────

    [Fact]
    public void ExtractStreamDelta_ReturnsTextDelta()
    {
        var chunk = """
        {
            "type": "content_block_delta",
            "index": 0,
            "delta": { "type": "text_delta", "text": "Hello" }
        }
        """;

        var delta = AnthropicResponseMapper.ExtractStreamDelta(chunk);
        Assert.Equal("Hello", delta);
    }

    [Fact]
    public void ExtractStreamDelta_ReturnsNull_ForMessageStart()
    {
        var chunk = """
        {
            "type": "message_start",
            "message": {
                "id": "msg_test",
                "type": "message",
                "role": "assistant",
                "model": "claude-sonnet-4-20250514",
                "content": [],
                "stop_reason": null,
                "usage": { "input_tokens": 10, "output_tokens": 0 }
            }
        }
        """;

        var delta = AnthropicResponseMapper.ExtractStreamDelta(chunk);
        Assert.Null(delta);
    }

    [Fact]
    public void ExtractStreamDelta_ReturnsNull_ForContentBlockStart()
    {
        var chunk = """
        {
            "type": "content_block_start",
            "index": 0,
            "content_block": { "type": "text", "text": "" }
        }
        """;

        var delta = AnthropicResponseMapper.ExtractStreamDelta(chunk);
        Assert.Null(delta);
    }

    [Fact]
    public void ExtractStreamDelta_ReturnsNull_ForInputJsonDelta()
    {
        var chunk = """
        {
            "type": "content_block_delta",
            "index": 1,
            "delta": { "type": "input_json_delta", "partial_json": "{\"city\":" }
        }
        """;

        var delta = AnthropicResponseMapper.ExtractStreamDelta(chunk);
        Assert.Null(delta);
    }

    [Fact]
    public void ExtractStreamDelta_ReturnsNull_ForMessageStop()
    {
        var chunk = """{ "type": "message_stop" }""";

        var delta = AnthropicResponseMapper.ExtractStreamDelta(chunk);
        Assert.Null(delta);
    }

    // ─── FromStreamedContent ────────────────────────────────────────

    [Fact]
    public void FromStreamedContent_BuildsResponse()
    {
        var response = AnthropicResponseMapper.FromStreamedContent(
            "accumulated text", TimeSpan.FromSeconds(1), "claude-sonnet-4-20250514");

        Assert.True(response.Success);
        Assert.Equal("accumulated text", response.Content);
        Assert.Equal("claude-sonnet-4-20250514", response.Model);
        Assert.Equal("end_turn", response.StopReason);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Latency);
    }
}