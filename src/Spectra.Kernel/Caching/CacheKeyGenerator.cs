using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spectra.Contracts.Providers;

namespace Spectra.Kernel.Caching;

/// <summary>
/// Generates deterministic cache keys from LLM requests by hashing
/// the semantically relevant fields.
/// </summary>
public static class CacheKeyGenerator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Generate(string prefix, LlmRequest request)
    {
        var fingerprint = new CacheFingerprint
        {
            Model = request.Model,
            Messages = request.Messages.Select(ToMessageFingerprint).ToList(),
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            StopSequence = request.StopSequence,
            OutputMode = request.OutputMode.ToString(),
            JsonSchema = request.JsonSchema,
            SystemPrompt = request.SystemPrompt,
            ToolNames = request.Tools?.Select(t => t.Name).OrderBy(n => n).ToList()
        };

        var json = JsonSerializer.Serialize(fingerprint, SerializerOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();

        return $"{prefix}{request.Model}:{hex}";
    }

    private static MessageFingerprint ToMessageFingerprint(LlmMessage msg) => new()
    {
        Role = msg.Role.ToString(),
        Content = msg.Content,
        ToolCallId = msg.ToolCallId,
        Name = msg.Name,
        ToolCalls = msg.ToolCalls?.Select(tc => new ToolCallFingerprint
        {
            Id = tc.Id,
            Name = tc.Name,
            Arguments = tc.Arguments
        }).ToList(),
        ContentParts = msg.ContentParts?.Select(cp => new ContentPartFingerprint
        {
            Type = cp.Type.ToString(),
            Text = cp.Text,
            MimeType = cp.MimeType,
            Data = cp.Data
        }).ToList()
    };

    private sealed class CacheFingerprint
    {
        public string Model { get; init; } = default!;
        public List<MessageFingerprint> Messages { get; init; } = default!;
        public double? Temperature { get; init; }
        public int? MaxTokens { get; init; }
        public string? StopSequence { get; init; }
        public string? OutputMode { get; init; }
        public string? JsonSchema { get; init; }
        public string? SystemPrompt { get; init; }
        public List<string>? ToolNames { get; init; }
    }

    private sealed class MessageFingerprint
    {
        public string Role { get; init; } = default!;
        public string? Content { get; init; }
        public string? ToolCallId { get; init; }
        public string? Name { get; init; }
        public List<ToolCallFingerprint>? ToolCalls { get; init; }
        public List<ContentPartFingerprint>? ContentParts { get; init; }
    }

    private sealed class ToolCallFingerprint
    {
        public string Id { get; init; } = default!;
        public string Name { get; init; } = default!;
        public Dictionary<string, object?> Arguments { get; init; } = default!;
    }

    private sealed class ContentPartFingerprint
    {
        public string Type { get; init; } = default!;
        public string? Text { get; init; }
        public string? MimeType { get; init; }
        public string? Data { get; init; }
    }
}