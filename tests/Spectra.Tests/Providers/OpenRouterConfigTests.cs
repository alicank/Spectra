using Spectra.Extensions.Providers.OpenRouter;
using Xunit;

namespace Spectra.Tests.Providers;

public class OpenRouterConfigTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var config = new OpenRouterConfig();

        Assert.Equal("openrouter", config.ProviderName);
        Assert.Equal("https://openrouter.ai/api/v1", config.BaseUrl);
        Assert.Equal("openai/gpt-4o", config.Model);
        Assert.Null(config.ApiKey);
        Assert.Null(config.SiteUrl);
        Assert.Null(config.SiteName);
    }

    [Fact]
    public void CapabilityDefaults_AreReasonable()
    {
        var caps = new OpenRouterCapabilitiesConfig();

        Assert.True(caps.SupportsJsonMode);
        Assert.True(caps.SupportsToolCalling);
        Assert.True(caps.SupportsVision);
        Assert.True(caps.SupportsStreaming);
        Assert.False(caps.SupportsAudio);
        Assert.False(caps.SupportsVideo);
        Assert.Null(caps.MaxContextTokens);
        Assert.Null(caps.MaxOutputTokens);
    }

    [Fact]
    public void Properties_AreSettable()
    {
        var config = new OpenRouterConfig
        {
            ProviderName = "custom",
            BaseUrl = "https://custom.example.com/v1",
            ApiKey = "sk-or-my-key",
            Model = "anthropic/claude-sonnet-4",
            SiteUrl = "https://myapp.com",
            SiteName = "MyApp"
        };

        Assert.Equal("custom", config.ProviderName);
        Assert.Equal("https://custom.example.com/v1", config.BaseUrl);
        Assert.Equal("sk-or-my-key", config.ApiKey);
        Assert.Equal("anthropic/claude-sonnet-4", config.Model);
        Assert.Equal("https://myapp.com", config.SiteUrl);
        Assert.Equal("MyApp", config.SiteName);
    }
}