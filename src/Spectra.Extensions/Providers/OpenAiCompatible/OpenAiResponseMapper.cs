using System.Diagnostics;
using System.Text.Json;
using Spectra.Contracts.Providers;

namespace Spectra.Extensions.Providers.OpenAiCompatible;

internal static class OpenAiResponseMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static LlmResponse MapCompletion(string json, TimeSpan latency)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var choice = root.GetProperty("choices")[0];
        var message = choice.GetProperty("message");

        var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? string.Empty
            : string.Empty;

        var stopReason = choice.TryGetProperty("finish_reason", out var fr)
            ? fr.GetString()
            : null;

        var model = root.TryGetProperty("model", out var m)
            ? m.GetString()
            : null;

        int? inputTokens = null;
        int? outputTokens = null;

        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt))
                inputTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ct))
                outputTokens = ct.GetInt32();
        }

        List<ToolCall>? toolCalls = null;

        if (message.TryGetProperty("tool_calls", out var tcArr) && tcArr.ValueKind == JsonValueKind.Array)
        {
            toolCalls = [];

            foreach (var tc in tcArr.EnumerateArray())
            {
                var function = tc.GetProperty("function");
                var name = function.GetProperty("name").GetString()!;
                var argsJson = function.GetProperty("arguments").GetString() ?? "{}";
                var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson, JsonOptions)
                           ?? new Dictionary<string, object?>();

                toolCalls.Add(new ToolCall
                {
                    Id = tc.GetProperty("id").GetString()!,
                    Name = name,
                    Arguments = args
                });
            }
        }

        return new LlmResponse
        {
            Content = content,
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
    /// Returns null when the chunk carries no content delta (e.g. role-only or finish chunk).
    /// </summary>
    internal static string? ExtractStreamDelta(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return null;

        var delta = choices[0].GetProperty("delta");

        if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            return content.GetString();

        return null;
    }

    /// <summary>
    /// Builds a final LlmResponse from the full accumulated text produced by streaming.
    /// Token counts are not available in streaming mode unless usage is included in the final chunk.
    /// </summary>
    internal static LlmResponse FromStreamedContent(string accumulatedContent, TimeSpan latency, string model)
    {
        return new LlmResponse
        {
            Content = accumulatedContent,
            Success = true,
            Model = model,
            Latency = latency,
            StopReason = "stop"
        };
    }
}