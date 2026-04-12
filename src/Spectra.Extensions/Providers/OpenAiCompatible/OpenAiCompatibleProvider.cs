using Spectra.Contracts.Providers;
using Spectra.Contracts.Workflow;

namespace Spectra.Extensions.Providers.OpenAiCompatible;

public sealed class OpenAiCompatibleProvider : ILlmProvider
{
    private readonly OpenAiConfig _config;
    private readonly IHttpClientFactory? _httpFactory;

    public string Name => _config.ProviderName;

    public OpenAiCompatibleProvider(OpenAiConfig config, IHttpClientFactory? httpFactory = null)
    {
        _config = config;
        _httpFactory = httpFactory;
    }

    public ILlmClient CreateClient(AgentDefinition agent)
    {
        var http = _httpFactory?.CreateClient(Name) ?? new HttpClient();
        var model = agent.Model ?? _config.Model;

        // Allow per-agent overrides
        var effectiveConfig = _config;

        if (agent.BaseUrlOverride is not null)
        {
            effectiveConfig = new OpenAiConfig
            {
                ProviderName = _config.ProviderName,
                BaseUrl = agent.BaseUrlOverride,
                ApiKey = _config.ApiKey,
                Model = _config.Model,
                ApiVersion = agent.ApiVersionOverride ?? _config.ApiVersion,
                Organization = _config.Organization,
                Capabilities = _config.Capabilities
            };
        }

        return new OpenAiCompatibleClient(http, effectiveConfig, model);
    }

    public bool SupportsModel(string modelId)
    {
        // OpenAI-compatible providers accept any model string —
        // the remote API will reject unknown models at call time.
        return true;
    }
}