using System.Text.Json;
using Spectra.Contracts.Providers;

namespace Spectra.Extensions.Providers.Ollama;

internal static class OllamaResponseMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Maps a non-streaming Ollama /api/chat response to an LlmResponse.
    /// Ollama returns a flat object: { model, message: { role, content, tool_calls? }, done, eval_count, prompt_eval_count, ... }
    /// </summary>
    internal static LlmResponse MapCompletion(string json, TimeSpan latency)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var message = root.GetProperty("message");

        var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? string.Empty
            : string.Empty;

        var model = root.TryGetProperty("model", out var m)
            ? m.GetString()
            : null;

        // Ollama uses eval_count (output tokens) and prompt_eval_count (input tokens)
        int? inputTokens = root.TryGetProperty("prompt_eval_count", out var pec)
            ? pec.GetInt32()
            : null;

        int? outputTokens = root.TryGetProperty("eval_count", out var ec)
            ? ec.GetInt32()
            : null;

        var stopReason = root.TryGetProperty("done_reason", out var dr)
            ? dr.GetString()
            : root.TryGetProperty("done", out var done) && done.GetBoolean()
                ? "stop"
                : null;

        List<ToolCall>? toolCalls = null;

        if (message.TryGetProperty("tool_calls", out var tcArr) && tcArr.ValueKind == JsonValueKind.Array)
        {
            toolCalls = [];

            foreach (var tc in tcArr.EnumerateArray())
            {
                var function = tc.GetProperty("function");
                var name = function.GetProperty("name").GetString()!;

                var args = new Dictionary<string, object?>();
                if (function.TryGetProperty("arguments", out var argsEl))
                {
                    if (argsEl.ValueKind == JsonValueKind.String)
                    {
                        args = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                                   argsEl.GetString()!, JsonOptions)
                               ?? new Dictionary<string, object?>();
                    }
                    else if (argsEl.ValueKind == JsonValueKind.Object)
                    {
                        args = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                                   argsEl.GetRawText(), JsonOptions)
                               ?? new Dictionary<string, object?>();
                    }
                }

                toolCalls.Add(new ToolCall
                {
                    Id = tc.TryGetProperty("id", out var id) ? id.GetString()! : Guid.NewGuid().ToString("N"),
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
    /// Extracts the text delta from a single newline-delimited JSON streaming chunk.
    /// Ollama streams each chunk as a complete JSON object: { message: { content: "..." }, done: false }
    /// Returns null when the chunk carries no content delta.
    /// </summary>
    internal static string? ExtractStreamDelta(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("message", out var message))
            return null;

        if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        return null;
    }

    /// <summary>
    /// Checks whether a streaming chunk is the final one (done = true).
    /// </summary>
    internal static bool IsStreamDone(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("done", out var done) && done.GetBoolean();
    }
}