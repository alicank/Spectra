using System.Text.Json;
using Spectra.Contracts.Providers;

namespace Spectra.Extensions.Providers.Gemini;

internal static class GeminiResponseMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static LlmResponse MapCompletion(string json, TimeSpan latency)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var contentText = string.Empty;
        List<ToolCall>? toolCalls = null;

        // Gemini returns { candidates: [{ content: { parts: [...] }, finishReason }], usageMetadata }
        if (root.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array &&
            candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];

            if (candidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array)
            {
                var textParts = new List<string>();

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text))
                    {
                        var t = text.GetString();
                        if (t is not null)
                            textParts.Add(t);
                    }
                    else if (part.TryGetProperty("functionCall", out var fc))
                    {
                        toolCalls ??= [];
                        var argsJson = fc.TryGetProperty("args", out var args)
                            ? args.GetRawText()
                            : "{}";
                        var parsedArgs = JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson, JsonOptions)
                                         ?? new Dictionary<string, object?>();

                        toolCalls.Add(new ToolCall
                        {
                            Id = $"call_{Guid.NewGuid():N}",
                            Name = fc.GetProperty("name").GetString()!,
                            Arguments = parsedArgs
                        });
                    }
                }

                contentText = string.Join(string.Empty, textParts);
            }
        }

        string? stopReason = null;
        if (root.TryGetProperty("candidates", out var cands2) &&
            cands2.GetArrayLength() > 0 &&
            cands2[0].TryGetProperty("finishReason", out var fr))
        {
            stopReason = fr.GetString();
        }

        int? inputTokens = null;
        int? outputTokens = null;

        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            if (usage.TryGetProperty("promptTokenCount", out var pt))
                inputTokens = pt.GetInt32();
            if (usage.TryGetProperty("candidatesTokenCount", out var ct))
                outputTokens = ct.GetInt32();
        }

        return new LlmResponse
        {
            Content = contentText,
            Success = true,
            Model = null, // Gemini does not echo the model in the response body
            StopReason = stopReason,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Latency = latency,
            ToolCalls = toolCalls
        };
    }

    /// <summary>
    /// Extracts the text delta from a single SSE chunk JSON payload.
    /// Gemini streams with the same structure as the non-streaming response
    /// but each chunk contains a partial candidates array.
    /// </summary>
    internal static string? ExtractStreamDelta(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
            return null;

        var candidate = candidates[0];

        if (!candidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array ||
            parts.GetArrayLength() == 0)
            return null;

        // Return the first text part found in this chunk
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text))
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
            StopReason = "STOP"
        };
    }
}