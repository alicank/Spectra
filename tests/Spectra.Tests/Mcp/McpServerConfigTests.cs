using Spectra.Contracts.Mcp;
using Xunit;

namespace Spectra.Tests.Mcp;

public class McpServerConfigTests
{
    [Fact]
    public void DefaultValues_AreEnterpriseSafe()
    {
        var config = new McpServerConfig
        {
            Name = "test",
            Command = "echo"
        };

        // Environment is NOT inherited by default (security)
        Assert.False(config.InheritEnvironment);

        // Read-only is off by default (usability)
        Assert.False(config.ReadOnly);

        // No rate limit by default
        Assert.Equal(0, config.MaxCallsPerSession);
        Assert.Equal(0, config.MaxConcurrentCalls);

        // 1MB response limit
        Assert.Equal(1_048_576, config.MaxResponseSizeBytes);

        // Free by default
        Assert.Equal(0m, config.CostPerCall);

        // No approval required by default
        Assert.False(config.RequireApproval);

        // Default transport is stdio
        Assert.Equal(McpTransportType.Stdio, config.Transport);
    }

    [Fact]
    public void EnvironmentVariables_DefaultsToEmpty()
    {
        var config = new McpServerConfig { Name = "test", Command = "echo" };

        Assert.Empty(config.EnvironmentVariables);
    }

    [Fact]
    public void Headers_DefaultsToEmpty()
    {
        var config = new McpServerConfig { Name = "test", Command = "echo" };

        Assert.Empty(config.Headers);
    }

    [Fact]
    public void AllowedAndDeniedTools_DefaultToNull()
    {
        var config = new McpServerConfig { Name = "test", Command = "echo" };

        Assert.Null(config.AllowedTools);
        Assert.Null(config.DeniedTools);
    }
}