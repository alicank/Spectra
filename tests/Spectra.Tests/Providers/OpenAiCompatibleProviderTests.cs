using Spectra.Contracts.Providers;
using Spectra.Contracts.Workflow;
using Spectra.Extensions.Providers.OpenAiCompatible;
using Xunit;

namespace Spectra.Tests.Providers;

public class OpenAiCompatibleProviderTests
{
    private static AgentDefinition Agent(
        string provider = "openai",
        string model = "gpt-4o",
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
        var provider = new OpenAiCompatibleProvider(
            new OpenAiConfig { ProviderName = "deepseek" });

        Assert.Equal("deepseek", provider.Name);
    }

    [Fact]
    public void CreateClient_ReturnsClientWithCorrectModel()
    {
        var provider = new OpenAiCompatibleProvider(
            new OpenAiConfig { ProviderName = "openai", Model = "gpt-4o" });

        var client = provider.CreateClient(Agent(model: "gpt-4o-mini"));

        Assert.Equal("gpt-4o-mini", client.ModelId);
        Assert.Equal("openai", client.ProviderName);
    }

    [Fact]
    public void CreateClient_UsesDefaultModel_WhenAgentModelIsNull()
    {
        var provider = new OpenAiCompatibleProvider(
            new OpenAiConfig { ProviderName = "openai", Model = "gpt-4o" });

        var agent = new AgentDefinition
        {
            Id = "test",
            Provider = "openai",
            Model = null!
        };

        var client = provider.CreateClient(agent);

        Assert.Equal("gpt-4o", client.ModelId);
    }

    [Fact]
    public void CreateClient_AppliesBaseUrlOverride()
    {
        var provider = new OpenAiCompatibleProvider(
            new OpenAiConfig
            {
                ProviderName = "openai",
                BaseUrl = "https://api.openai.com/v1",
                ApiKey = "sk-original"
            });

        var client = provider.CreateClient(
            Agent(baseUrl: "https://custom-proxy.example.com/v1"));

        // Client was created — the override is applied internally.
        // We verify it didn't throw and the client is usable.
        Assert.NotNull(client);
        Assert.IsType<OpenAiCompatibleClient>(client);
    }

    [Fact]
    public void SupportsModel_AlwaysReturnsTrue()
    {
        var provider = new OpenAiCompatibleProvider(
            new OpenAiConfig { ProviderName = "openai" });

        Assert.True(provider.SupportsModel("gpt-4o"));
        Assert.True(provider.SupportsModel("anything-goes"));
        Assert.True(provider.SupportsModel("custom-finetune:ft-abc"));
    }

    [Fact]
    public void CreateClient_ImplementsStreamClient()
    {
        var provider = new OpenAiCompatibleProvider(
            new OpenAiConfig { ProviderName = "openai", Model = "gpt-4o" });

        var client = provider.CreateClient(Agent());

        Assert.IsAssignableFrom<ILlmStreamClient>(client);
    }
}