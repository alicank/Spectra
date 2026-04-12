namespace Spectra.Extensions.Providers.Gemini;

public class GeminiConfig
{
    /// <summary>
    /// Display name used to match against AgentDefinition.Provider.
    /// </summary>
    public string ProviderName { get; set; } = "gemini";

    /// <summary>
    /// Base URL for the Gemini API. Must NOT include model or method path segments.
    /// The client appends /models/{model}:generateContent (or :streamGenerateContent).
    /// </summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>
    /// API key appended as ?key= query parameter.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Default model to use when AgentDefinition does not specify one.
    /// </summary>
    public string Model { get; set; } = "gemini-2.0-flash";

    /// <summary>
    /// Static model capabilities advertised by clients created from this config.
    /// </summary>
    public GeminiCapabilitiesConfig Capabilities { get; set; } = new();
}

public class GeminiCapabilitiesConfig
{
    public bool SupportsJsonMode { get; set; } = true;
    public bool SupportsToolCalling { get; set; } = true;
    public bool SupportsVision { get; set; } = true;
    public bool SupportsStreaming { get; set; } = true;
    public bool SupportsAudio { get; set; } = true;
    public bool SupportsVideo { get; set; } = true;
    public int? MaxContextTokens { get; set; } = 1_048_576;
    public int? MaxOutputTokens { get; set; } = 8_192;
}