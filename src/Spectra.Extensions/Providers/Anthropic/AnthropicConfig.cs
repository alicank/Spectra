namespace Spectra.Extensions.Providers.Anthropic;

public class AnthropicConfig
{
    /// <summary>
    /// Display name used to match against AgentDefinition.Provider.
    /// </summary>
    public string ProviderName { get; set; } = "anthropic";

    /// <summary>
    /// Base URL for the Anthropic Messages API. Must NOT include the /v1/messages path segment.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1";

    /// <summary>
    /// API key sent via the x-api-key header.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Default model to use when AgentDefinition does not specify one.
    /// </summary>
    public string Model { get; set; } = "claude-sonnet-4-20250514";

    /// <summary>
    /// Anthropic API version header. Defaults to 2023-06-01.
    /// </summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";

    /// <summary>
    /// Static model capabilities advertised by clients created from this config.
    /// </summary>
    public AnthropicCapabilitiesConfig Capabilities { get; set; } = new();
}

public class AnthropicCapabilitiesConfig
{
    public bool SupportsJsonMode { get; set; } = true;
    public bool SupportsToolCalling { get; set; } = true;
    public bool SupportsVision { get; set; } = true;
    public bool SupportsStreaming { get; set; } = true;
    public bool SupportsAudio { get; set; }
    public bool SupportsVideo { get; set; }
    public int? MaxContextTokens { get; set; } = 200_000;
    public int? MaxOutputTokens { get; set; } = 8_192;
}