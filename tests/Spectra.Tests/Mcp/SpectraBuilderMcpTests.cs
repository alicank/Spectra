using Spectra.Contracts.Mcp;
using Spectra.Registration;
using Xunit;

namespace Spectra.Tests.Mcp;

/// <summary>
/// Tests for SpectraBuilder MCP server registration.
/// </summary>
public class SpectraBuilderMcpTests
{
    [Fact]
    public void AddMcpServer_WithConfig_AddsToCollection()
    {
        var builder = new SpectraBuilder();

        builder.AddMcpServer(new McpServerConfig
        {
            Name = "filesystem",
            Command = "npx",
            Arguments = ["-y", "@modelcontextprotocol/server-filesystem"]
        });

        Assert.Single(builder.McpServers);
        Assert.Equal("filesystem", builder.McpServers[0].Name);
    }

    [Fact]
    public void AddMcpServer_WithFluentBuilder_AddsToCollection()
    {
        var builder = new SpectraBuilder();

        builder.AddMcpServer("github", mcp =>
        {
            mcp.UseSse("https://mcp.github.com/sse")
               .WithBearerToken("ghp_test123")
               .AsReadOnly()
               .WithMaxCallsPerSession(100);
        });

        Assert.Single(builder.McpServers);
        var config = builder.McpServers[0];
        Assert.Equal("github", config.Name);
        Assert.Equal(McpTransportType.Sse, config.Transport);
        Assert.True(config.ReadOnly);
        Assert.Equal(100, config.MaxCallsPerSession);
    }

    [Fact]
    public void AddMcpServer_CanRegisterMultipleServers()
    {
        var builder = new SpectraBuilder();

        builder.AddMcpServer("server1", mcp => mcp.UseStdio("echo"));
        builder.AddMcpServer("server2", mcp => mcp.UseSse("https://example.com/mcp"));
        builder.AddMcpServer(new McpServerConfig { Name = "server3", Command = "node" });

        Assert.Equal(3, builder.McpServers.Count);
    }

    [Fact]
    public void AddMcpServer_ReturnsSameBuilder_ForChaining()
    {
        var builder = new SpectraBuilder();

        var result = builder.AddMcpServer("s", mcp => mcp.UseStdio("echo"));

        Assert.Same(builder, result);
    }

    [Fact]
    public void AddMcpServer_FluentBuilder_CompleteEnterpriseConfig()
    {
        var builder = new SpectraBuilder();

        builder.AddMcpServer("production-db", mcp =>
        {
            mcp.UseStdio("node", "db-server.js")
               .WithWorkingDirectory("/opt/mcp-servers")
               .WithEnvironment("DB_HOST", "prod-db.internal")
               .WithEnvironment("DB_PORT", "5432")
               .AsReadOnly()
               .WithAllowedTools("query", "describe_table", "list_tables")
               .WithDeniedTools("drop_table")
               .WithMaxCallsPerSession(500)
               .WithMaxConcurrentCalls(5)
               .WithMaxResponseSize(5_242_880) // 5MB
               .WithCostPerCall(0.001m)
               .WithRequireApproval()
               .WithResilience(new McpResilienceOptions
               {
                   MaxRetries = 3,
                   Timeout = TimeSpan.FromSeconds(60),
                   CircuitBreakerThreshold = 10
               });
        });

        var config = builder.McpServers[0];
        Assert.Equal("production-db", config.Name);
        Assert.Equal("node", config.Command);
        Assert.Equal("/opt/mcp-servers", config.WorkingDirectory);
        Assert.False(config.InheritEnvironment);
        Assert.Equal("prod-db.internal", config.EnvironmentVariables["DB_HOST"]);
        Assert.True(config.ReadOnly);
        Assert.Equal(3, config.AllowedTools!.Count);
        Assert.Single(config.DeniedTools!);
        Assert.Equal(500, config.MaxCallsPerSession);
        Assert.Equal(5, config.MaxConcurrentCalls);
        Assert.Equal(5_242_880, config.MaxResponseSizeBytes);
        Assert.Equal(0.001m, config.CostPerCall);
        Assert.True(config.RequireApproval);
        Assert.Equal(3, config.Resilience!.MaxRetries);
        Assert.Equal(10, config.Resilience.CircuitBreakerThreshold);
    }
}