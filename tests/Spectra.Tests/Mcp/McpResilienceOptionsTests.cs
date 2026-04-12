using Spectra.Contracts.Mcp;
using Xunit;

namespace Spectra.Tests.Mcp;

public class McpResilienceOptionsTests
{
    [Fact]
    public void DefaultValues_AreReasonable()
    {
        var options = new McpResilienceOptions();

        Assert.Equal(2, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), options.BaseDelay);
        Assert.Equal(TimeSpan.FromSeconds(15), options.MaxDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
        Assert.True(options.UseExponentialBackoff);
        Assert.True(options.RestartOnCrash);
        Assert.Equal(5, options.CircuitBreakerThreshold);
        Assert.Equal(TimeSpan.FromSeconds(60), options.CircuitBreakerCooldown);
    }

    [Fact]
    public void CanOverrideAllSettings()
    {
        var options = new McpResilienceOptions
        {
            MaxRetries = 5,
            BaseDelay = TimeSpan.FromMilliseconds(500),
            MaxDelay = TimeSpan.FromSeconds(60),
            Timeout = TimeSpan.FromMinutes(2),
            UseExponentialBackoff = false,
            RestartOnCrash = false,
            CircuitBreakerThreshold = 10,
            CircuitBreakerCooldown = TimeSpan.FromMinutes(5)
        };

        Assert.Equal(5, options.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.BaseDelay);
        Assert.Equal(TimeSpan.FromSeconds(60), options.MaxDelay);
        Assert.Equal(TimeSpan.FromMinutes(2), options.Timeout);
        Assert.False(options.UseExponentialBackoff);
        Assert.False(options.RestartOnCrash);
        Assert.Equal(10, options.CircuitBreakerThreshold);
        Assert.Equal(TimeSpan.FromMinutes(5), options.CircuitBreakerCooldown);
    }

    [Fact]
    public void Record_SupportsWith()
    {
        var original = new McpResilienceOptions();
        var modified = original with { MaxRetries = 0, Timeout = TimeSpan.FromSeconds(5) };

        Assert.Equal(0, modified.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(5), modified.Timeout);
        // Original unchanged
        Assert.Equal(2, original.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(30), original.Timeout);
    }
}