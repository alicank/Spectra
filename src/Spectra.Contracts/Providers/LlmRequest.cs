using Spectra.Contracts.Tools;

namespace Spectra.Contracts.Providers;

public class LlmRequest
{
    public required string Model { get; init; }
    public required List<LlmMessage> Messages { get; init; }

    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public string? StopSequence { get; init; }

    public LlmOutputMode OutputMode { get; init; } = LlmOutputMode.Text;
    public string? JsonSchema { get; init; }

    public string? SystemPrompt { get; init; }

    public List<ToolDefinition>? Tools { get; set; }

    /// <summary>
    /// Per-request override to bypass the cache. When true, the caching decorator
    /// skips lookup and storage for this request regardless of global options.
    /// Useful for workflow steps that need fresh responses.
    /// </summary>
    public bool SkipCache { get; init; }
}

public class LlmMessage
{
    public required LlmRole Role { get; init; }
    public string? Content { get; init; }

    public List<MediaContent>? ContentParts { get; init; }

    public bool HasMedia => ContentParts?.Any(p => p.Type != MediaType.Text) == true;

    public List<ToolCall>? ToolCalls { get; init; }
    public string? ToolCallId { get; init; }
    public string? Name { get; init; }

    public static LlmMessage ToolResult(string toolCallId, string content)
        => new() { Role = LlmRole.Tool, ToolCallId = toolCallId, Content = content };

    public static LlmMessage FromText(LlmRole role, string content)
        => new() { Role = role, Content = content };

    public class MediaContent
    {
        public required MediaType Type { get; init; }
        public string? Text { get; init; }
        public string? Data { get; init; }
        public string? MimeType { get; init; }
        public string? SourceUri { get; init; }

        public static MediaContent FromText(string text)
            => new() { Type = MediaType.Text, Text = text };

        public static MediaContent FromImage(string base64, string mimeType = "image/png")
            => new() { Type = MediaType.Image, Data = base64, MimeType = mimeType };

        public static MediaContent FromAudio(string base64, string mimeType = "audio/wav")
            => new() { Type = MediaType.Audio, Data = base64, MimeType = mimeType };

        public static MediaContent FromVideo(string base64, string mimeType = "video/mp4")
            => new() { Type = MediaType.Video, Data = base64, MimeType = mimeType };
    }

    public enum MediaType
    {
        Text,
        Image,
        Audio,
        Video
    }
}

public enum LlmRole
{
    System,
    User,
    Assistant,
    Tool
}

public enum LlmOutputMode
{
    Text,
    Json,
    StructuredJson
}