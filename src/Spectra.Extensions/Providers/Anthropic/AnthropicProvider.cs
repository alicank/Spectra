using Spectra.Contracts.Providers;
using Spectra.Contracts.Workflow;

namespace Spectra.Extensions.Providers.Anthropic;

public sealed class AnthropicProvider : ILlmProvider
{
    private readonly AnthropicConfig _config;
    private readonly IHttpClientFactory? _httpFactory;

    public string Name => _config.ProviderName;

    public AnthropicProvider(AnthropicConfig config, IHttpClientFactory? httpFactory = null)
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
            effectiveConfig = new AnthropicConfig
            {
                ProviderName = _config.ProviderName,
                BaseUrl = agent.BaseUrlOverride,
                ApiKey = _config.ApiKey,
                Model = _config.Model,
                AnthropicVersion = _config.AnthropicVersion,
                Capabilities = _config.Capabilities
            };
        }

        return new AnthropicClient(http, effectiveConfig, model);
    }

    public bool SupportsModel(string modelId)
    {
        return modelId.StartsWith("claude", StringComparison.OrdinalIgnoreCase);
    }
}