using Spectra.Contracts.Mcp;
using Spectra.Registration;
using Xunit;

namespace Spectra.Tests.Mcp;

public class McpServerConfigBuilderTests
{
    // ── Stdio ──

    [Fact]
    public void UseStdio_SetsTransportAndCommand()
    {
        var builder = new McpServerConfigBuilder("test-server");
        builder.UseStdio("npx", "-y", "@modelcontextprotocol/server-filesystem", "/tmp");

        var config = builder.Build();

        Assert.Equal("test-server", config.Name);
        Assert.Equal(McpTransportType.Stdio, config.Transport);
        Assert.Equal("npx", config.Command);
        Assert.Equal(3, config.Arguments.Count);
        Assert.Equal("-y", config.Arguments[0]);
        Assert.Equal("/tmp", config.Arguments[2]);
    }

    [Fact]
    public void UseStdio_WithWorkingDirectory()
    {
        var builder = new McpServerConfigBuilder("s");
        builder.UseStdio("node", "server.js")
               .WithWorkingDirectory("/opt/mcp");

        var config = builder.Build();

        Assert.Equal("/opt/mcp", config.WorkingDirectory);
    }

    [Fact]
    public void UseStdio_WithEnvironment()
    {
        var builder = new McpServerConfigBuilder("s");
        builder.UseStdio("node", "server.js")
               .WithEnvironment("API_KEY", "secret-123")
               .WithEnvironment("NODE_ENV", "production");

        var config = builder.Build();

        Assert.Equal(2, config.EnvironmentVariables.Count);
        Assert.Equal("secret-123", config.EnvironmentVariables["API_KEY"]);
        Assert.Equal("production", config.EnvironmentVariables["NODE_ENV"]);
    }

    [Fact]
    public void InheritEnvironment_DefaultsFalse()
    {
        var builder = new McpServerConfigBuilder("s");
        builder.UseStdio("echo");

        Assert.False(builder.Build().InheritEnvironment);
    }

    [Fact]
    public void WithInheritEnvironment_SetsTrue()
    {
        var builder = new McpServerConfigBuilder("s");
        builder.UseStdio("echo").WithInheritEnvironment();

        Assert.True(builder.Build().InheritEnvironment);
    }

    // ── SSE ──

    [Fact]
    public void UseSse_SetsTransportAndUrl()
    {
        var builder = new McpServerConfigBuilder("remote");
        builder.UseSse("https://api.example.com/mcp/sse");

        var config = builder.Build();

        Assert.Equal(McpTransportType.Sse, config.Transport);
        Assert.Equal("https://api.example.com/mcp/sse", config.Url);
    }

    [Fact]
    public void UseSse_WithHeaders()
    {
        var builder = new McpServerConfigBuilder("remote");
        builder.UseSse("https://api.example.com/mcp/sse")
               .WithHeader("X-Custom", "value")
               .WithBearerToken("my-token");

        var config = builder.Build();

        Assert.Equal("value", config.Headers["X-Custom"]);
        Assert.Equal("Bearer my-token", config.Headers["Authorization"]);
    }

    // ── HTTP ──

    [Fact]
    public void UseHttp_SetsTransportAndUrl()
    {
        var builder = new McpServerConfigBuilder("stream");
        builder.UseHttp("https://api.example.com/mcp");

        var config = builder.Build();

        Assert.Equal(McpTransportType.Http, config.Transport);
        Assert.Equal("https://api.example.com/mcp", config.Url);
    }

    // ── Guardrails ──

    [Fact]
    public void WithAllowedTools_SetsToolWhitelist()
    {
        var builder = new McpServerConfigBuilder("s");
        builder.UseStdio("echo")
               .WithAllowedTools("read_file", "list_dir");

        var config = builder.Build();

        Assert.NotNull(config.AllowedTools);
        Assert.Equal(2, config.AllowedTools!.Count);
        Assert.Contains("read_file", config.AllowedTools);
    }

    [Fact]
    public void WithDeniedTools_SetsToolBlacklist()
    {
        var builder = new McpServerConfigBuilder("s");
        builder.UseStdio("echo")
               .WithDeniedTools("delete_file", "format_disk");

        var config = builder.Build();

        Assert.NotNull(config.DeniedTools);
        Assert.Equal(2, config.DeniedTools!.Count);
    }

    [Fact]
    public void AsReadOnly_SetsReadOnlyFlag()
    {
        var builder = new McpServerConfigBuilder("s");
        builder.UseStdio("echo").AsReadOnly();

        Assert.True(builder.Build().ReadOnly);
    }

    [Fact]
    public void WithMaxCallsPerSession_SetsRateLimit()
    {
        var builder = new McpServerConfigBuilder("s");
        builder.UseStdio("echo").WithMaxCallsPerSession(50);

        Assert.Equal(50, builder.Build().MaxCallsPerSession);
    }

    [Fact]
    public void WithMaxConcurrentCalls_SetsConcurrencyLimit()
    {
        var builder = new McpServerConfigBuilder("s");
        builder.UseStdio("echo").WithMaxConcurrentCalls(5);

        Assert.Equal(5, builder.Build().MaxConcurrentCalls);
    }

    [Fact]
    public void WithMaxResponseSize_SetsLimit()
    {
        var builder = new McpServerConfigBuilder("s");
        builder.UseStdio("echo").WithMaxResponseSize(512_000);

        Assert.Equal(512_000, builder.Build().MaxResponseSizeBytes);
    }

    [Fact]
    public void WithCostPerCall_SetsCost()
    {
        var builder = new McpServerConfigBuilder("s");
        builder.UseStdio("echo").WithCostPerCall(0.05m);

        Assert.Equal(0.05m, builder.Build().CostPerCall);
    }

    [Fact]
    public void WithRequireApproval_SetsFlag()
    {
        var builder = new McpServerConfigBuilder("s");
        builder.UseStdio("echo").WithRequireApproval();

        Assert.True(builder.Build().RequireApproval);
    }

    // ── Resilience ──

    [Fact]
    public void WithResilience_SetsOptions()
    {
        var builder = new McpServerConfigBuilder("s");
        builder.UseStdio("echo")
               .WithResilience(new McpResilienceOptions
               {
                   MaxRetries = 5,
                   Timeout = TimeSpan.FromSeconds(10)
               });

        var config = builder.Build();

        Assert.NotNull(config.Resilience);
        Assert.Equal(5, config.Resilience!.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(10), config.Resilience.Timeout);
    }

    // ── Fluent chaining ──

    [Fact]
    public void FluentChaining_BuildsCompleteConfig()
    {
        var builder = new McpServerConfigBuilder("production-fs");
        builder.UseStdio("npx", "-y", "@modelcontextprotocol/server-filesystem", "/data")
               .WithWorkingDirectory("/opt/mcp")
               .WithEnvironment("LOG_LEVEL", "warn")
               .AsReadOnly()
               .WithMaxCallsPerSession(100)
               .WithMaxConcurrentCalls(3)
               .WithMaxResponseSize(2_097_152)
               .WithCostPerCall(0.01m)
               .WithResilience(new McpResilienceOptions { MaxRetries = 3 });

        var config = builder.Build();

        Assert.Equal("production-fs", config.Name);
        Assert.Equal(McpTransportType.Stdio, config.Transport);
        Assert.Equal("npx", config.Command);
        Assert.Equal("/opt/mcp", config.WorkingDirectory);
        Assert.Equal("warn", config.EnvironmentVariables["LOG_LEVEL"]);
        Assert.False(config.InheritEnvironment);
        Assert.True(config.ReadOnly);
        Assert.Equal(100, config.MaxCallsPerSession);
        Assert.Equal(3, config.MaxConcurrentCalls);
        Assert.Equal(2_097_152, config.MaxResponseSizeBytes);
        Assert.Equal(0.01m, config.CostPerCall);
        Assert.Equal(3, config.Resilience!.MaxRetries);
    }
}