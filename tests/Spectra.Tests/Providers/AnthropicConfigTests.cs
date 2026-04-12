using Spectra.Extensions.Providers.Anthropic;
using Xunit;

namespace Spectra.Tests.Providers;

public class AnthropicConfigTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var config = new AnthropicConfig();

        Assert.Equal("anthropic", config.ProviderName);
        Assert.Equal("https://api.anthropic.com/v1", config.BaseUrl);
        Assert.Equal("claude-sonnet-4-20250514", config.Model);
        Assert.Equal("2023-06-01", config.AnthropicVersion);
        Assert.Null(config.ApiKey);
    }

    [Fact]
    public void CapabilityDefaults_AreReasonable()
    {
        var caps = new AnthropicCapabilitiesConfig();

        Assert.True(caps.SupportsJsonMode);
        Assert.True(caps.SupportsToolCalling);
        Assert.True(caps.SupportsVision);
        Assert.True(caps.SupportsStreaming);
        Assert.False(caps.SupportsAudio);
        Assert.False(caps.SupportsVideo);
        Assert.Equal(200_000, caps.MaxContextTokens);
        Assert.Equal(8_192, caps.MaxOutputTokens);
    }
}