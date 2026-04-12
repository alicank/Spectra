using System.Text.Json;
using Spectra.Contracts.Providers;

namespace Spectra.Extensions.Providers.Anthropic;

internal static class AnthropicResponseMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static LlmResponse MapCompletion(string json, TimeSpan latency)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Anthropic returns { content: [...], stop_reason, model, usage }
        var contentText = string.Empty;
        List<ToolCall>? toolCalls = null;

        if (root.TryGetProperty("content", out var contentArr) && contentArr.ValueKind == JsonValueKind.Array)
        {
            var textParts = new List<string>();

            foreach (var block in contentArr.EnumerateArray())
            {
                var blockType = block.GetProperty("type").GetString();

                switch (blockType)
                {
                    case "text":
                        var text = block.GetProperty("text").GetString();
                        if (text is not null)
                            textParts.Add(text);
                        break;

                    case "tool_use":
                        toolCalls ??= [];
                        var inputJson = block.GetProperty("input").GetRawText();
                        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(inputJson, JsonOptions)
                                   ?? new Dictionary<string, object?>();

                        toolCalls.Add(new ToolCall
                        {
                            Id = block.GetProperty("id").GetString()!,
                            Name = block.GetProperty("name").GetString()!,
                            Arguments = args
                        });
                        break;
                }
            }

            contentText = string.Join(string.Empty, textParts);
        }

        var stopReason = root.TryGetProperty("stop_reason", out var sr)
            ? sr.GetString()
            : null;

        var model = root.TryGetProperty("model", out var m)
            ? m.GetString()
            : null;

        int? inputTokens = null;
        int? outputTokens = null;

        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var it))
                inputTokens = it.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var ot))
                outputTokens = ot.GetInt32();
        }

        return new LlmResponse
        {
            Content = contentText,
            Success = true,
            Model = model,
            StopReason = stopReason,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Latency = latency,
            ToolCalls = toolCalls
        };
    }

    /// <summary>
    /// Extracts the text delta from a single SSE chunk JSON payload.
    /// Anthropic streams with event types: message_start, content_block_start,
    /// content_block_delta, content_block_stop, message_delta, message_stop.
    /// We extract text from content_block_delta events with type "text_delta".
    /// </summary>
    internal static string? ExtractStreamDelta(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var eventType))
            return null;

        var type = eventType.GetString();

        // content_block_delta → { type: "content_block_delta", delta: { type: "text_delta", text: "..." } }
        if (type == "content_block_delta" &&
            root.TryGetProperty("delta", out var delta) &&
            delta.TryGetProperty("type", out var deltaType) &&
            deltaType.GetString() == "text_delta" &&
            delta.TryGetProperty("text", out var text))
        {
            return text.GetString();
        }

        return null;
    }

    /// <summary>
    /// Builds a final LlmResponse from the full accumulated text produced by streaming.
    /// </summary>
    internal static LlmResponse FromStreamedContent(string accumulatedContent, TimeSpan latency, string model)
    {
        return new LlmResponse
        {
            Content = accumulatedContent,
            Success = true,
            Model = model,
            Latency = latency,
            StopReason = "end_turn"
        };
    }
}