using Spectra.Contracts.Providers;
using Spectra.Contracts.Workflow;

namespace Spectra.Extensions.Providers.Ollama;

public sealed class OllamaProvider : ILlmProvider
{
    private readonly OllamaConfig _config;
    private readonly IHttpClientFactory? _httpFactory;

    public string Name => _config.ProviderName;

    public OllamaProvider(OllamaConfig config, IHttpClientFactory? httpFactory = null)
    {
        _config = config;
        _httpFactory = httpFactory;
    }

    public ILlmClient CreateClient(AgentDefinition agent)
    {
        var http = _httpFactory?.CreateClient(Name) ?? new HttpClient();
        var model = agent.Model ?? _config.Model;

        var effectiveConfig = _config;

        if (agent.BaseUrlOverride is not null)
        {
            effectiveConfig = new OllamaConfig
            {
                ProviderName = _config.ProviderName,
                Host = agent.BaseUrlOverride,
                Model = _config.Model,
                KeepAlive = _config.KeepAlive,
                Options = _config.Options,
                Capabilities = _config.Capabilities
            };
        }

        return new OllamaClient(http, effectiveConfig, model);
    }

    public bool SupportsModel(string modelId)
    {
        // Ollama accepts any model string — the server will reject unknown models at call time.
        return true;
    }
}