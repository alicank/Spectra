using Spectra.Contracts.Providers;
using Spectra.Contracts.Workflow;

namespace Spectra.Extensions.Providers.Gemini;

public sealed class GeminiProvider : ILlmProvider
{
    private readonly GeminiConfig _config;
    private readonly IHttpClientFactory? _httpFactory;

    public string Name => _config.ProviderName;

    public GeminiProvider(GeminiConfig config, IHttpClientFactory? httpFactory = null)
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
            effectiveConfig = new GeminiConfig
            {
                ProviderName = _config.ProviderName,
                BaseUrl = agent.BaseUrlOverride,
                ApiKey = _config.ApiKey,
                Model = _config.Model,
                Capabilities = _config.Capabilities
            };
        }

        return new GeminiClient(http, effectiveConfig, model);
    }

    public bool SupportsModel(string modelId)
    {
        return modelId.StartsWith("gemini", StringComparison.OrdinalIgnoreCase);
    }
}