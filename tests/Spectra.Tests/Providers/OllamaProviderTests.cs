using Spectra.Contracts.Providers;
using Spectra.Contracts.Workflow;
using Spectra.Extensions.Providers.Ollama;
using Xunit;

namespace Spectra.Tests.Providers;

public class OllamaProviderTests
{
    private static AgentDefinition Agent(
        string provider = "ollama",
        string model = "llama3",
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
        var provider = new OllamaProvider(
            new OllamaConfig { ProviderName = "ollama" });

        Assert.Equal("ollama", provider.Name);
    }

    [Fact]
    public void CreateClient_ReturnsClientWithCorrectModel()
    {
        var provider = new OllamaProvider(
            new OllamaConfig { ProviderName = "ollama", Model = "llama3" });

        var client = provider.CreateClient(Agent(model: "mistral"));

        Assert.Equal("mistral", client.ModelId);
        Assert.Equal("ollama", client.ProviderName);
    }

    [Fact]
    public void CreateClient_UsesDefaultModel_WhenAgentModelIsNull()
    {
        var provider = new OllamaProvider(
            new OllamaConfig { ProviderName = "ollama", Model = "llama3" });

        var agent = new AgentDefinition
        {
            Id = "test",
            Provider = "ollama",
            Model = null!
        };

        var client = provider.CreateClient(agent);

        Assert.Equal("llama3", client.ModelId);
    }

    [Fact]
    public void CreateClient_AppliesBaseUrlOverride()
    {
        var provider = new OllamaProvider(
            new OllamaConfig
            {
                ProviderName = "ollama",
                Host = "http://localhost:11434",
                KeepAlive = "5m",
                Options = new Dictionary<string, object?> { ["num_ctx"] = 4096 }
            });

        var client = provider.CreateClient(
            Agent(baseUrl: "http://remote-ollama:11434"));

        Assert.NotNull(client);
        Assert.IsType<OllamaClient>(client);
    }

    [Fact]
    public void SupportsModel_AlwaysReturnsTrue()
    {
        var provider = new OllamaProvider(
            new OllamaConfig { ProviderName = "ollama" });

        Assert.True(provider.SupportsModel("llama3"));
        Assert.True(provider.SupportsModel("anything-goes"));
        Assert.True(provider.SupportsModel("custom-model:latest"));
    }

    [Fact]
    public void CreateClient_ImplementsStreamClient()
    {
        var provider = new OllamaProvider(
            new OllamaConfig { ProviderName = "ollama", Model = "llama3" });

        var client = provider.CreateClient(Agent());

        Assert.IsAssignableFrom<ILlmStreamClient>(client);
    }

    [Fact]
    public void CreateClient_PropagatesCapabilitiesFromConfig()
    {
        var provider = new OllamaProvider(
            new OllamaConfig
            {
                ProviderName = "ollama",
                Model = "llama3",
                Capabilities = new OllamaCapabilitiesConfig
                {
                    SupportsJsonMode = true,
                    SupportsToolCalling = false,
                    SupportsVision = true,
                    SupportsStreaming = true,
                    MaxContextTokens = 8192,
                    MaxOutputTokens = 2048
                }
            });

        var client = provider.CreateClient(Agent());

        Assert.True(client.Capabilities.SupportsJsonMode);
        Assert.False(client.Capabilities.SupportsToolCalling);
        Assert.True(client.Capabilities.SupportsVision);
        Assert.True(client.Capabilities.SupportsStreaming);
        Assert.Equal(8192, client.Capabilities.MaxContextTokens);
        Assert.Equal(2048, client.Capabilities.MaxOutputTokens);
    }
}