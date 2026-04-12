using Spectra.Contracts.Providers;
using Spectra.Contracts.Workflow;

namespace Spectra.Extensions.Providers.OpenRouter;

public sealed class OpenRouterProvider : ILlmProvider
{
    private readonly OpenRouterConfig _config;
    private readonly IHttpClientFactory? _httpFactory;

    public string Name => _config.ProviderName;

    public OpenRouterProvider(OpenRouterConfig config, IHttpClientFactory? httpFactory = null)
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
            effectiveConfig = new OpenRouterConfig
            {
                ProviderName = _config.ProviderName,
                BaseUrl = agent.BaseUrlOverride,
                ApiKey = _config.ApiKey,
                Model = _config.Model,
                SiteUrl = _config.SiteUrl,
                SiteName = _config.SiteName,
                Capabilities = _config.Capabilities
            };
        }

        return new OpenRouterClient(http, effectiveConfig, model);
    }

    public bool SupportsModel(string modelId)
    {
        // OpenRouter accepts any model string in "provider/model" format —
        // the API will reject unknown models at call time.
        return true;
    }
}