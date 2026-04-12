using Spectra.Extensions.Providers.Gemini;
using Xunit;

namespace Spectra.Tests.Providers;

public class GeminiConfigTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var config = new GeminiConfig();

        Assert.Equal("gemini", config.ProviderName);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta", config.BaseUrl);
        Assert.Null(config.ApiKey);
        Assert.Equal("gemini-2.0-flash", config.Model);
    }

    [Fact]
    public void CapabilityDefaults_AreCorrect()
    {
        var caps = new GeminiCapabilitiesConfig();

        Assert.True(caps.SupportsJsonMode);
        Assert.True(caps.SupportsToolCalling);
        Assert.True(caps.SupportsVision);
        Assert.True(caps.SupportsStreaming);
        Assert.True(caps.SupportsAudio);
        Assert.True(caps.SupportsVideo);
        Assert.Equal(1_048_576, caps.MaxContextTokens);
        Assert.Equal(8_192, caps.MaxOutputTokens);
    }

    [Fact]
    public void Properties_AreSettable()
    {
        var config = new GeminiConfig
        {
            ProviderName = "custom",
            BaseUrl = "https://custom.example.com/v1",
            ApiKey = "my-key",
            Model = "gemini-1.5-pro"
        };

        Assert.Equal("custom", config.ProviderName);
        Assert.Equal("https://custom.example.com/v1", config.BaseUrl);
        Assert.Equal("my-key", config.ApiKey);
        Assert.Equal("gemini-1.5-pro", config.Model);
    }
}