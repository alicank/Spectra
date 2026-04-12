using Spectra.Extensions.Providers.OpenAiCompatible;
using Xunit;

namespace Spectra.Tests.Providers;

public class OpenAiConfigTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var config = new OpenAiConfig();

        Assert.Equal("openai", config.ProviderName);
        Assert.Equal("https://api.openai.com/v1", config.BaseUrl);
        Assert.Equal("gpt-4o", config.Model);
        Assert.Null(config.ApiKey);
        Assert.Null(config.ApiVersion);
        Assert.Null(config.Organization);
    }

    [Fact]
    public void CapabilityDefaults_AreReasonable()
    {
        var caps = new ModelCapabilitiesConfig();

        Assert.True(caps.SupportsJsonMode);
        Assert.True(caps.SupportsToolCalling);
        Assert.True(caps.SupportsVision);
        Assert.True(caps.SupportsStreaming);
        Assert.False(caps.SupportsAudio);
        Assert.False(caps.SupportsVideo);
        Assert.Null(caps.MaxContextTokens);
        Assert.Null(caps.MaxOutputTokens);
    }
}