using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Contracts.Events;
using Spectra.Contracts.Mcp;
using Spectra.Contracts.State;
using Spectra.Contracts.Tools;
using Spectra.Kernel.Execution;
using Spectra.Registration;
using System.Reflection;
using Xunit;

namespace Spectra.Tests.Registration;

public class SpectraHostedServiceTests
{
    private readonly InMemoryToolRegistry _toolRegistry = new();
    private readonly NullEventSink _eventSink = NullEventSink.Instance;
    private readonly ILogger<SpectraHostedService> _logger = NullLogger<SpectraHostedService>.Instance;

    // ── Constructor Validation ───────────────────────────────────

    [Fact]
    public void Ctor_ThrowsOnNullMcpConfigs()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SpectraHostedService(null!, [], _toolRegistry, _eventSink, _logger));
    }

    [Fact]
    public void Ctor_ThrowsOnNullToolAssemblies()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SpectraHostedService([], null!, _toolRegistry, _eventSink, _logger));
    }

    [Fact]
    public void Ctor_ThrowsOnNullToolRegistry()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SpectraHostedService([], [], null!, _eventSink, _logger));
    }

    // ── Assembly Tool Discovery ─────────────────────────────────

    [Fact]
    public async Task StartAsync_WithNoAssembliesOrServers_CompletesSuccessfully()
    {
        var service = CreateService();

        await service.StartAsync(CancellationToken.None);

        // No exception — service started with nothing to do
    }

    [Fact]
    public async Task StartAsync_WithAssembly_DiscoverTools()
    {
        // Use the test assembly which has no [SpectraTool]-decorated types
        var assembly = typeof(SpectraHostedServiceTests).Assembly;
        var service = CreateService(toolAssemblies: [assembly]);

        await service.StartAsync(CancellationToken.None);

        // Completes without error — 0 tools discovered from test assembly is fine
    }

    // ── Cancellation ────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_RespectsAlreadyCancelledToken()
    {
        var config = new McpServerConfig
        {
            Name = "test",
            Transport = McpTransportType.Stdio,
            Command = "echo"
        };
        var service = CreateService(mcpConfigs: [config]);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.StartAsync(cts.Token));
    }

    // ── Dispose ─────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var service = CreateService();
        await service.StartAsync(CancellationToken.None);

        await service.DisposeAsync();
        await service.DisposeAsync(); // Should not throw
    }

    [Fact]
    public async Task StopAsync_CallsDisposeAsync()
    {
        var service = CreateService();
        await service.StartAsync(CancellationToken.None);

        await service.StopAsync(CancellationToken.None);

        // Calling dispose again should be idempotent (already disposed by StopAsync)
        await service.DisposeAsync();
    }

    // ── DI Registration ─────────────────────────────────────────

    [Fact]
    public void AddSpectra_WithToolAssemblies_RegistersHostedService()
    {
        var services = new ServiceCollection();

        services.AddSpectra(s =>
        {
            s.AddToolsFromAssembly(typeof(SpectraHostedServiceTests).Assembly);
        });

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IHostedService));

        Assert.NotNull(descriptor);
    }

    [Fact]
    public void AddSpectra_WithMcpAndAssemblies_RegistersSingleHostedService()
    {
        var services = new ServiceCollection();

        services.AddSpectra(s =>
        {
            s.AddToolsFromAssembly(typeof(SpectraHostedServiceTests).Assembly);
            s.AddMcpServer("test", mcp => mcp.UseStdio("echo", "hello"));
        });

        var descriptors = services.Where(d =>
            d.ServiceType == typeof(IHostedService)).ToList();

        Assert.Single(descriptors);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private SpectraHostedService CreateService(
        IReadOnlyList<McpServerConfig>? mcpConfigs = null,
        IReadOnlyList<Assembly>? toolAssemblies = null)
    {
        return new SpectraHostedService(
            mcpConfigs ?? [],
            toolAssemblies ?? [],
            _toolRegistry,
            _eventSink,
            _logger);
    }
}