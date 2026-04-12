using Spectra.Extensions.Providers.Gemini;
using Xunit;

namespace Spectra.Tests.Providers;

public class GeminiResponseMapperTests
{
    // ─── completion mapping ─────────────────────────────────────────

    [Fact]
    public void MapCompletion_ExtractsContent()
    {
        var json = """
        {
            "candidates": [{
                "content": {
                    "parts": [{ "text": "Hello world" }],
                    "role": "model"
                },
                "finishReason": "STOP"
            }],
            "usageMetadata": {
                "promptTokenCount": 10,
                "candidatesTokenCount": 5,
                "totalTokenCount": 15
            }
        }
        """;

        var response = GeminiResponseMapper.MapCompletion(json, TimeSpan.FromMilliseconds(200));

        Assert.True(response.Success);
        Assert.Equal("Hello world", response.Content);
        Assert.Equal("STOP", response.StopReason);
        Assert.Equal(10, response.InputTokens);
        Assert.Equal(5, response.OutputTokens);
        Assert.Equal(TimeSpan.FromMilliseconds(200), response.Latency);
        Assert.False(response.HasToolCalls);
    }

    [Fact]
    public void MapCompletion_HandlesEmptyParts()
    {
        var json = """
        {
            "candidates": [{
                "content": {
                    "parts": [],
                    "role": "model"
                },
                "finishReason": "STOP"
            }]
        }
        """;

        var response = GeminiResponseMapper.MapCompletion(json, TimeSpan.Zero);

        Assert.True(response.Success);
        Assert.Equal(string.Empty, response.Content);
    }

    [Fact]
    public void MapCompletion_ConcatsMultipleTextParts()
    {
        var json = """
        {
            "candidates": [{
                "content": {
                    "parts": [
                        { "text": "First part. " },
                        { "text": "Second part." }
                    ],
                    "role": "model"
                },
                "finishReason": "STOP"
            }]
        }
        """;

        var response = GeminiResponseMapper.MapCompletion(json, TimeSpan.Zero);

        Assert.Equal("First part. Second part.", response.Content);
    }

    [Fact]
    public void MapCompletion_ExtractsFunctionCalls()
    {
        var json = """
        {
            "candidates": [{
                "content": {
                    "parts": [{
                        "functionCall": {
                            "name": "get_weather",
                            "args": { "city": "Paris" }
                        }
                    }],
                    "role": "model"
                },
                "finishReason": "STOP"
            }],
            "usageMetadata": {
                "promptTokenCount": 15,
                "candidatesTokenCount": 20,
                "totalTokenCount": 35
            }
        }
        """;

        var response = GeminiResponseMapper.MapCompletion(json, TimeSpan.Zero);

        Assert.True(response.Success);
        Assert.True(response.HasToolCalls);
        Assert.Single(response.ToolCalls!);

        var tc = response.ToolCalls![0];
        Assert.Equal("get_weather", tc.Name);
        Assert.True(tc.Arguments.ContainsKey("city"));
        Assert.NotNull(tc.Id); // auto-generated
    }

    [Fact]
    public void MapCompletion_MultipleFunctionCalls()
    {
        var json = """
        {
            "candidates": [{
                "content": {
                    "parts": [
                        { "text": "Let me look that up." },
                        { "functionCall": { "name": "tool_a", "args": {} } },
                        { "functionCall": { "name": "tool_b", "args": { "x": 1 } } }
                    ],
                    "role": "model"
                },
                "finishReason": "STOP"
            }]
        }
        """;

        var response = GeminiResponseMapper.MapCompletion(json, TimeSpan.Zero);

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
            "candidates": [{
                "content": {
                    "parts": [{ "text": "hi" }],
                    "role": "model"
                },
                "finishReason": "STOP"
            }]
        }
        """;

        var response = GeminiResponseMapper.MapCompletion(json, TimeSpan.Zero);

        Assert.Null(response.InputTokens);
        Assert.Null(response.OutputTokens);
    }

    [Fact]
    public void MapCompletion_NoCandidates_ReturnsEmpty()
    {
        var json = """{ "candidates": [] }""";

        var response = GeminiResponseMapper.MapCompletion(json, TimeSpan.Zero);

        Assert.True(response.Success);
        Assert.Equal(string.Empty, response.Content);
    }

    // ─── stream delta extraction ────────────────────────────────────

    [Fact]
    public void ExtractStreamDelta_ReturnsTextDelta()
    {
        var chunk = """
        {
            "candidates": [{
                "content": {
                    "parts": [{ "text": "Hello" }],
                    "role": "model"
                }
            }]
        }
        """;

        var delta = GeminiResponseMapper.ExtractStreamDelta(chunk);
        Assert.Equal("Hello", delta);
    }

    [Fact]
    public void ExtractStreamDelta_ReturnsNull_ForFunctionCallChunk()
    {
        var chunk = """
        {
            "candidates": [{
                "content": {
                    "parts": [{
                        "functionCall": { "name": "test", "args": {} }
                    }],
                    "role": "model"
                }
            }]
        }
        """;

        var delta = GeminiResponseMapper.ExtractStreamDelta(chunk);
        Assert.Null(delta);
    }

    [Fact]
    public void ExtractStreamDelta_ReturnsNull_ForEmptyCandidates()
    {
        var chunk = """{ "candidates": [] }""";

        var delta = GeminiResponseMapper.ExtractStreamDelta(chunk);
        Assert.Null(delta);
    }

    [Fact]
    public void ExtractStreamDelta_ReturnsNull_ForNoParts()
    {
        var chunk = """
        {
            "candidates": [{
                "content": { "parts": [], "role": "model" }
            }]
        }
        """;

        var delta = GeminiResponseMapper.ExtractStreamDelta(chunk);
        Assert.Null(delta);
    }

    // ─── FromStreamedContent ────────────────────────────────────────

    [Fact]
    public void FromStreamedContent_BuildsResponse()
    {
        var response = GeminiResponseMapper.FromStreamedContent(
            "accumulated text", TimeSpan.FromSeconds(1), "gemini-2.0-flash");

        Assert.True(response.Success);
        Assert.Equal("accumulated text", response.Content);
        Assert.Equal("gemini-2.0-flash", response.Model);
        Assert.Equal("STOP", response.StopReason);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Latency);
    }
}