namespace Spectra.Contracts.Providers;

public class ModelCapabilities
{
    public bool SupportsJsonMode { get; init; }
    public bool SupportsToolCalling { get; init; }
    public bool SupportsVision { get; init; }
    public bool SupportsAudio { get; init; }
    public bool SupportsVideo { get; init; }
    public bool SupportsStreaming { get; init; }
    public int? MaxContextTokens { get; init; }
    public int? MaxOutputTokens { get; init; }
}