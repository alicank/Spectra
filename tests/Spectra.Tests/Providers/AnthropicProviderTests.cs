using Spectra.Contracts.Providers;
using Spectra.Contracts.Workflow;
using Spectra.Extensions.Providers.Anthropic;
using Xunit;

namespace Spectra.Tests.Providers;

public class AnthropicProviderTests
{
    private static AgentDefinition Agent(
        string provider = "anthropic",
        string model = "claude-sonnet-4-20250514",
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
        var provider = new AnthropicProvider(
            new AnthropicConfig { ProviderName = "anthropic" });

        Assert.Equal("anthropic", provider.Name);
    }

    [Fact]
    public void CreateClient_ReturnsClientWithCorrectModel()
    {
        var provider = new AnthropicProvider(
            new AnthropicConfig { ProviderName = "anthropic", Model = "claude-sonnet-4-20250514" });

        var client = provider.CreateClient(Agent(model: "claude-haiku-3"));

        Assert.Equal("claude-haiku-3", client.ModelId);
        Assert.Equal("anthropic", client.ProviderName);
    }

    [Fact]
    public void CreateClient_UsesDefaultModel_WhenAgentModelIsNull()
    {
        var provider = new AnthropicProvider(
            new AnthropicConfig { ProviderName = "anthropic", Model = "claude-sonnet-4-20250514" });

        var agent = new AgentDefinition
        {
            Id = "test",
            Provider = "anthropic",
            Model = null!
        };

        var client = provider.CreateClient(agent);

        Assert.Equal("claude-sonnet-4-20250514", client.ModelId);
    }

    [Fact]
    public void CreateClient_AppliesBaseUrlOverride()
    {
        var provider = new AnthropicProvider(
            new AnthropicConfig
            {
                ProviderName = "anthropic",
                BaseUrl = "https://api.anthropic.com/v1",
                ApiKey = "sk-ant-original"
            });

        var client = provider.CreateClient(
            Agent(baseUrl: "https://custom-proxy.example.com/v1"));

        Assert.NotNull(client);
        Assert.IsType<AnthropicClient>(client);
    }

    [Fact]
    public void SupportsModel_ReturnsTrueForClaudeModels()
    {
        var provider = new AnthropicProvider(
            new AnthropicConfig { ProviderName = "anthropic" });

        Assert.True(provider.SupportsModel("claude-sonnet-4-20250514"));
        Assert.True(provider.SupportsModel("claude-3-haiku-20240307"));
        Assert.True(provider.SupportsModel("claude-opus-4-20250514"));
        Assert.True(provider.SupportsModel("Claude-Custom"));
    }

    [Fact]
    public void SupportsModel_ReturnsFalseForNonClaudeModels()
    {
        var provider = new AnthropicProvider(
            new AnthropicConfig { ProviderName = "anthropic" });

        Assert.False(provider.SupportsModel("gpt-4o"));
        Assert.False(provider.SupportsModel("gemini-pro"));
        Assert.False(provider.SupportsModel("llama3"));
    }

    [Fact]
    public void CreateClient_ImplementsStreamClient()
    {
        var provider = new AnthropicProvider(
            new AnthropicConfig { ProviderName = "anthropic", Model = "claude-sonnet-4-20250514" });

        var client = provider.CreateClient(Agent());

        Assert.IsAssignableFrom<ILlmStreamClient>(client);
    }
}