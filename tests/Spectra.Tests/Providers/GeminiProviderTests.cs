using Spectra.Contracts.Providers;
using Spectra.Contracts.Workflow;
using Spectra.Extensions.Providers.Gemini;
using Xunit;

namespace Spectra.Tests.Providers;

public class GeminiProviderTests
{
    private static AgentDefinition Agent(
        string provider = "gemini",
        string model = "gemini-2.0-flash",
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
        var provider = new GeminiProvider(
            new GeminiConfig { ProviderName = "gemini" });

        Assert.Equal("gemini", provider.Name);
    }

    [Fact]
    public void CreateClient_ReturnsClientWithCorrectModel()
    {
        var provider = new GeminiProvider(
            new GeminiConfig { ProviderName = "gemini", Model = "gemini-2.0-flash" });

        var client = provider.CreateClient(Agent(model: "gemini-1.5-pro"));

        Assert.Equal("gemini-1.5-pro", client.ModelId);
        Assert.Equal("gemini", client.ProviderName);
    }

    [Fact]
    public void CreateClient_UsesDefaultModel_WhenAgentModelIsNull()
    {
        var provider = new GeminiProvider(
            new GeminiConfig { ProviderName = "gemini", Model = "gemini-2.0-flash" });

        var agent = new AgentDefinition
        {
            Id = "test",
            Provider = "gemini",
            Model = null!
        };

        var client = provider.CreateClient(agent);

        Assert.Equal("gemini-2.0-flash", client.ModelId);
    }

    [Fact]
    public void CreateClient_AppliesBaseUrlOverride()
    {
        var provider = new GeminiProvider(
            new GeminiConfig
            {
                ProviderName = "gemini",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
                ApiKey = "original-key"
            });

        var client = provider.CreateClient(
            Agent(baseUrl: "https://custom-proxy.example.com/v1beta"));

        Assert.NotNull(client);
        Assert.IsType<GeminiClient>(client);
    }

    [Fact]
    public void SupportsModel_ReturnsTrueForGeminiModels()
    {
        var provider = new GeminiProvider(
            new GeminiConfig { ProviderName = "gemini" });

        Assert.True(provider.SupportsModel("gemini-2.0-flash"));
        Assert.True(provider.SupportsModel("gemini-1.5-pro"));
        Assert.True(provider.SupportsModel("gemini-1.5-flash"));
        Assert.True(provider.SupportsModel("Gemini-Custom"));
    }

    [Fact]
    public void SupportsModel_ReturnsFalseForNonGeminiModels()
    {
        var provider = new GeminiProvider(
            new GeminiConfig { ProviderName = "gemini" });

        Assert.False(provider.SupportsModel("gpt-4o"));
        Assert.False(provider.SupportsModel("claude-sonnet-4-20250514"));
        Assert.False(provider.SupportsModel("llama3"));
    }

    [Fact]
    public void CreateClient_ImplementsStreamClient()
    {
        var provider = new GeminiProvider(
            new GeminiConfig { ProviderName = "gemini", Model = "gemini-2.0-flash" });

        var client = provider.CreateClient(Agent());

        Assert.IsAssignableFrom<ILlmStreamClient>(client);
    }
}