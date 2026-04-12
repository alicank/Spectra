namespace Spectra.Extensions.Providers.OpenAiCompatible;

public class OpenAiConfig
{
    /// <summary>
    /// Display name used to match against AgentDefinition.Provider (e.g. "openai", "deepseek").
    /// </summary>
    public string ProviderName { get; set; } = "openai";

    /// <summary>
    /// Base URL for the API. Must NOT include the /chat/completions path segment.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>
    /// API key sent as a Bearer token. Can be null for local endpoints (e.g. Ollama).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Default model to use when AgentDefinition does not specify one.
    /// </summary>
    public string Model { get; set; } = "gpt-4o";

    /// <summary>
    /// Optional API version header (used by Azure OpenAI).
    /// </summary>
    public string? ApiVersion { get; set; }

    /// <summary>
    /// Optional organization header for OpenAI.
    /// </summary>
    public string? Organization { get; set; }

    /// <summary>
    /// Static model capabilities advertised by clients created from this config.
    /// Override per-provider when the defaults don't match.
    /// </summary>
    public ModelCapabilitiesConfig Capabilities { get; set; } = new();
}

public class ModelCapabilitiesConfig
{
    public bool SupportsJsonMode { get; set; } = true;
    public bool SupportsToolCalling { get; set; } = true;
    public bool SupportsVision { get; set; } = true;
    public bool SupportsStreaming { get; set; } = true;
    public bool SupportsAudio { get; set; }
    public bool SupportsVideo { get; set; }
    public int? MaxContextTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
}