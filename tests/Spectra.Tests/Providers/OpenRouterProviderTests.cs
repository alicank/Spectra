using Spectra.Contracts.Providers;
using Spectra.Contracts.Workflow;
using Spectra.Extensions.Providers.OpenRouter;
using Xunit;

namespace Spectra.Tests.Providers;

public class OpenRouterProviderTests
{
    private static AgentDefinition Agent(
        string provider = "openrouter",
        string model = "openai/gpt-4o",
        string? baseUrl = null) => new()
        {
            Id = "test-agent",
            Provider = provider,
            Model = model,
            BaseUrlOverride = baseUrl
        };

    [Fact]
    public void Name_MatchesConfig()
    {
        var provider = new OpenRouterProvider(
            new OpenRouterConfig { ProviderName = "openrouter" });

        Assert.Equal("openrouter", provider.Name);
    }

    [Fact]
    public void CreateClient_ReturnsClientWithCorrectModel()
    {
        var provider = new OpenRouterProvider(
            new OpenRouterConfig { ProviderName = "openrouter", Model = "openai/gpt-4o" });

        var client = provider.CreateClient(Agent(model: "anthropic/claude-sonnet-4"));

        Assert.Equal("anthropic/claude-sonnet-4", client.ModelId);
        Assert.Equal("openrouter", client.ProviderName);
    }

    [Fact]
    public void CreateClient_UsesDefaultModel_WhenAgentModelIsNull()
    {
        var provider = new OpenRouterProvider(
            new OpenRouterConfig { ProviderName = "openrouter", Model = "openai/gpt-4o" });

        var agent = new AgentDefinition
        {
            Id = "test",
            Provider = "openrouter",
            Model = null!
        };

        var client = provider.CreateClient(agent);

        Assert.Equal("openai/gpt-4o", client.ModelId);
    }

    [Fact]
    public void CreateClient_AppliesBaseUrlOverride()
    {
        var provider = new OpenRouterProvider(
            new OpenRouterConfig
            {
                ProviderName = "openrouter",
                BaseUrl = "https://openrouter.ai/api/v1",
                ApiKey = "sk-or-original",
                SiteUrl = "https://myapp.com",
                SiteName = "MyApp"
            });

        var client = provider.CreateClient(
            Agent(baseUrl: "https://custom-proxy.example.com/v1"));

        Assert.NotNull(client);
        Assert.IsType<OpenRouterClient>(client);
    }

    [Fact]
    public void SupportsModel_AlwaysReturnsTrue()
    {
        var provider = new OpenRouterProvider(
            new OpenRouterConfig { ProviderName = "openrouter" });

        Assert.True(provider.SupportsModel("openai/gpt-4o"));
        Assert.True(provider.SupportsModel("anthropic/claude-sonnet-4"));
        Assert.True(provider.SupportsModel("google/gemini-2.0-flash"));
        Assert.True(provider.SupportsModel("meta-llama/llama-3-70b"));
        Assert.True(provider.SupportsModel("anything/goes"));
    }

    [Fact]
    public void CreateClient_ImplementsStreamClient()
    {
        var provider = new OpenRouterProvider(
            new OpenRouterConfig { ProviderName = "openrouter", Model = "openai/gpt-4o" });

        var client = provider.CreateClient(Agent());

        Assert.IsAssignableFrom<ILlmStreamClient>(client);
    }

    [Fact]
    public void CreateClient_PropagatesCapabilitiesFromConfig()
    {
        var provider = new OpenRouterProvider(
            new OpenRouterConfig
            {
                ProviderName = "openrouter",
                Model = "openai/gpt-4o",
                Capabilities = new OpenRouterCapabilitiesConfig
                {
                    SupportsJsonMode = true,
                    SupportsToolCalling = true,
                    SupportsVision = true,
                    SupportsStreaming = true,
                    SupportsAudio = false,
                    MaxContextTokens = 128_000,
                    MaxOutputTokens = 4_096
                }
            });

        var client = provider.CreateClient(Agent());

        Assert.True(client.Capabilities.SupportsJsonMode);
        Assert.True(client.Capabilities.SupportsToolCalling);
        Assert.True(client.Capabilities.SupportsVision);
        Assert.True(client.Capabilities.SupportsStreaming);
        Assert.False(client.Capabilities.SupportsAudio);
        Assert.Equal(128_000, client.Capabilities.MaxContextTokens);
        Assert.Equal(4_096, client.Capabilities.MaxOutputTokens);
    }
}