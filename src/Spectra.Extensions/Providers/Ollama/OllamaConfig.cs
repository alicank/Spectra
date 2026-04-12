namespace Spectra.Extensions.Providers.Ollama;

public class OllamaConfig
{
    /// <summary>
    /// Display name used to match against AgentDefinition.Provider.
    /// </summary>
    public string ProviderName { get; set; } = "ollama";

    /// <summary>
    /// Ollama host URL. Must NOT include the /api/chat path segment.
    /// </summary>
    public string Host { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Default model to use when AgentDefinition does not specify one.
    /// </summary>
    public string Model { get; set; } = "llama3";

    /// <summary>
    /// How long the model stays loaded in memory after a request (e.g. "5m", "24h", "-1" for indefinite).
    /// Null uses the Ollama server default.
    /// </summary>
    public string? KeepAlive { get; set; }

    /// <summary>
    /// Additional Ollama model options (temperature, top_p, num_ctx, etc.).
    /// Keys map directly to the Ollama "options" object.
    /// </summary>
    public Dictionary<string, object?> Options { get; set; } = new();

    /// <summary>
    /// Static model capabilities advertised by clients created from this config.
    /// </summary>
    public OllamaCapabilitiesConfig Capabilities { get; set; } = new();
}

public class OllamaCapabilitiesConfig
{
    public bool SupportsJsonMode { get; set; } = true;
    public bool SupportsToolCalling { get; set; } = true;
    public bool SupportsVision { get; set; } = false;
    public bool SupportsStreaming { get; set; } = true;
    public bool SupportsAudio { get; set; }
    public bool SupportsVideo { get; set; }
    public int? MaxContextTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
}