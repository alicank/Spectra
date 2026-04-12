namespace Spectra.Extensions.Providers.OpenRouter;

public class OpenRouterConfig
{
    /// <summary>
    /// Display name used to match against AgentDefinition.Provider.
    /// </summary>
    public string ProviderName { get; set; } = "openrouter";

    /// <summary>
    /// Base URL for the OpenRouter API. Must NOT include the /chat/completions path segment.
    /// </summary>
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>
    /// OpenRouter API key sent as a Bearer token.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Default model to use when AgentDefinition does not specify one.
    /// Uses the OpenRouter model identifier format (e.g. "anthropic/claude-sonnet-4").
    /// </summary>
    public string Model { get; set; } = "openai/gpt-4o";

    /// <summary>
    /// Your site URL sent via the HTTP-Referer header.
    /// Recommended by OpenRouter for ranking on openrouter.ai/rankings.
    /// </summary>
    public string? SiteUrl { get; set; }

    /// <summary>
    /// Your app name sent via the X-Title header.
    /// Shown on openrouter.ai/rankings.
    /// </summary>
    public string? SiteName { get; set; }

    /// <summary>
    /// Static model capabilities advertised by clients created from this config.
    /// Override when the defaults don't match.
    /// </summary>
    public OpenRouterCapabilitiesConfig Capabilities { get; set; } = new();
}

public class OpenRouterCapabilitiesConfig
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