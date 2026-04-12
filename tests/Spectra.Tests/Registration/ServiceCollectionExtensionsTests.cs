using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Evaluation;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.Memory;
using Spectra.Contracts.Prompts;
using Spectra.Contracts.Providers;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Tools;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Scheduling;
using Spectra.Registration;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Spectra.Tests.Registration;

public class ServiceCollectionExtensionsTests
{
    // ── Core Services ──────────────────────────────────────────────

    [Fact]
    public void AddSpectra_RegistersCoreServices()
    {
        var services = new ServiceCollection();

        services.AddSpectra(_ => { });

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IWorkflowRunner>());
        Assert.NotNull(provider.GetService<IStepRegistry>());
        Assert.NotNull(provider.GetService<IToolRegistry>());
        Assert.NotNull(provider.GetService<IProviderRegistry>());
        Assert.NotNull(provider.GetService<IAgentRegistry>());
        Assert.NotNull(provider.GetService<IStateMapper>());
        Assert.NotNull(provider.GetService<IConditionEvaluator>());
        Assert.NotNull(provider.GetService<IEventSink>());
    }

    [Fact]
    public void AddSpectra_RegistersParallelScheduler()
    {
        var services = new ServiceCollection();

        services.AddSpectra(_ => { });

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<ParallelScheduler>());
    }

    [Fact]
    public void AddSpectra_CoreServicesAreSingletons()
    {
        var services = new ServiceCollection();

        services.AddSpectra(_ => { });

        var provider = services.BuildServiceProvider();

        var runner1 = provider.GetService<IWorkflowRunner>();
        var runner2 = provider.GetService<IWorkflowRunner>();
        Assert.Same(runner1, runner2);

        var steps1 = provider.GetService<IStepRegistry>();
        var steps2 = provider.GetService<IStepRegistry>();
        Assert.Same(steps1, steps2);
    }

    // ── Null Arguments ────────────────────────────────────────────

    [Fact]
    public void AddSpectra_ThrowsOnNullServices()
    {
        IServiceCollection services = null!;

        Assert.Throws<ArgumentNullException>(() => services.AddSpectra(_ => { }));
    }

    [Fact]
    public void AddSpectra_ThrowsOnNullConfigure()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddSpectra(null!));
    }

    // ── Event Sinks ───────────────────────────────────────────────

    [Fact]
    public async Task AddSpectra_NoEventSinks_RegistersNullEventSink()
    {
        var services = new ServiceCollection();

        services.AddSpectra(_ => { });

        var provider = services.BuildServiceProvider();
        var sink = provider.GetRequiredService<IEventSink>();

        Assert.NotNull(sink);
        // Should not throw — it's a no-op
        await sink.PublishAsync(new TestEvent(), CancellationToken.None);
    }

    [Fact]
    public void AddSpectra_SingleEventSink_RegistersDirectly()
    {
        var consoleSink = new ConsoleEventSink();
        var services = new ServiceCollection();

        services.AddSpectra(s => s.AddEventSink(consoleSink));

        var provider = services.BuildServiceProvider();
        var sink = provider.GetRequiredService<IEventSink>();

        Assert.Same(consoleSink, sink);
    }

    [Fact]
    public void AddSpectra_MultipleEventSinks_RegistersComposite()
    {
        var services = new ServiceCollection();

        services.AddSpectra(s =>
        {
            s.AddConsoleEvents();
            s.AddEventSink(new ConsoleEventSink());
        });

        var provider = services.BuildServiceProvider();
        var sink = provider.GetRequiredService<IEventSink>();

        Assert.IsType<CompositeEventSink>(sink);
    }

    // ── Providers ─────────────────────────────────────────────────

    [Fact]
    public void AddSpectra_RegistersConfiguredProviders()
    {
        var stubProvider = new StubLlmProvider("test-provider");
        var services = new ServiceCollection();

        services.AddSpectra(s => s.AddProvider(stubProvider));

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IProviderRegistry>();

        Assert.NotNull(registry.GetProvider("test-provider"));
    }

    // ── Tools ─────────────────────────────────────────────────────

    [Fact]
    public void AddSpectra_RegistersExplicitTools()
    {
        var tool = new StubTool("my-tool");
        var services = new ServiceCollection();

        services.AddSpectra(s => s.AddTool(tool));

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IToolRegistry>();

        Assert.Same(tool, registry.Get("my-tool"));
    }

    // ── Optional Services: Present ────────────────────────────────

    [Fact]
    public void AddSpectra_WithCheckpointStore_Registers()
    {
        var services = new ServiceCollection();

        services.AddSpectra(s => s.AddInMemoryCheckpoints());

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<ICheckpointStore>());
        Assert.NotNull(provider.GetService<CheckpointOptions>());
    }

    [Fact]
    public void AddSpectra_WithMemoryStore_Registers()
    {
        var services = new ServiceCollection();

        services.AddSpectra(s => s.AddInMemoryMemory());

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IMemoryStore>());
    }

    [Fact]
    public void AddSpectra_WithPromptRegistry_Registers()
    {
        var registry = new StubPromptRegistry();
        var services = new ServiceCollection();

        services.AddSpectra(s => s.AddPrompts(registry));

        var provider = services.BuildServiceProvider();

        Assert.Same(registry, provider.GetService<IPromptRegistry>());
    }

    [Fact]
    public void AddSpectra_WithInterruptHandler_Registers()
    {
        var handler = new StubInterruptHandler();
        var services = new ServiceCollection();

        services.AddSpectra(s => s.AddInterruptHandler(handler));

        var provider = services.BuildServiceProvider();

        Assert.Same(handler, provider.GetService<IInterruptHandler>());
    }

    [Fact]
    public void AddSpectra_WithStateReducerRegistry_Registers()
    {
        var registry = new StubStateReducerRegistry();
        var services = new ServiceCollection();

        services.AddSpectra(s => s.AddStateReducers(registry));

        var provider = services.BuildServiceProvider();

        Assert.Same(registry, provider.GetService<IStateReducerRegistry>());
    }

    // ── Optional Services: Absent ─────────────────────────────────

    [Fact]
    public void AddSpectra_WithoutOptionalServices_RegistersDefaultsAndLeavesOthersNull()
    {
        var services = new ServiceCollection();

        services.AddSpectra(_ => { });

        var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<ICheckpointStore>());
        Assert.Null(provider.GetService<IMemoryStore>());
        Assert.NotNull(provider.GetService<IPromptRegistry>());
        Assert.Null(provider.GetService<IInterruptHandler>());
        Assert.Null(provider.GetService<IStateReducerRegistry>());
    }

    // ── Workflow Store ───────────────────────────────────────────

    [Fact]
    public void AddSpectra_WithWorkflowStore_Registers()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"spectra_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var services = new ServiceCollection();

            services.AddSpectra(s => s.AddWorkflowsFromDirectory(dir));

            var provider = services.BuildServiceProvider();

            Assert.NotNull(provider.GetService<IWorkflowStore>());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AddSpectra_WithoutWorkflowStore_ReturnsNull()
    {
        var services = new ServiceCollection();

        services.AddSpectra(_ => { });

        var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IWorkflowStore>());
    }

    // ── CheckpointOptions Always Registered ───────────────────────

    [Fact]
    public void AddSpectra_CheckpointOptions_AlwaysRegistered()
    {
        var services = new ServiceCollection();

        services.AddSpectra(_ => { });

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<CheckpointOptions>());
    }

    // ── Parallelism ───────────────────────────────────────────────

    [Fact]
    public void AddSpectra_ConfigureParallelism_FlowsToScheduler()
    {
        var services = new ServiceCollection();

        services.AddSpectra(s => s.ConfigureParallelism(16));

        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<ParallelScheduler>();

        Assert.NotNull(scheduler);
    }

    // ── Steps ─────────────────────────────────────────────────────

    [Fact]
    public void AddSpectra_WithSteps_RegistersInStepRegistry()
    {
        var step = new StubStep("test-step");
        var services = new ServiceCollection();

        services.AddSpectra(s => s.AddStep(step));

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IStepRegistry>();

        Assert.Same(step, registry.GetStep("test-step"));
    }

    [Fact]
    public void AddSpectra_WithStepFactory_ResolvesFromServiceProvider()
    {
        var step = new StubStep("factory-step");
        var services = new ServiceCollection();

        services.AddSpectra(s => s.AddStep(_ => step));

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IStepRegistry>();

        Assert.Same(step, registry.GetStep("factory-step"));
    }

    // ── MCP Hosted Service ────────────────────────────────────────

    [Fact]
    public void AddSpectra_WithMcpServers_RegistersHostedService()
    {
        var services = new ServiceCollection();

        services.AddSpectra(s =>
        {
            s.AddMcpServer("test-server", mcp => mcp.UseStdio("echo", "hello"));
        });

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IHostedService));

        Assert.NotNull(descriptor);
    }

    [Fact]
    public void AddSpectra_WithoutMcpServers_RegistersHostedService()
    {
        var services = new ServiceCollection();

        services.AddSpectra(_ => { });

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IHostedService));

        Assert.NotNull(descriptor);
    }

    // ── TryAdd Semantics ──────────────────────────────────────────

    [Fact]
    public void AddSpectra_DoesNotOverrideExistingRegistrations()
    {
        var customMapper = new StubStateMapper();
        var services = new ServiceCollection();

        services.AddSingleton<IStateMapper>(customMapper);
        services.AddSpectra(_ => { });

        var provider = services.BuildServiceProvider();

        Assert.Same(customMapper, provider.GetService<IStateMapper>());
    }

    // ── Global Agent Registration ───────────────────────────────────

    [Fact]
    public void AddSpectra_WithAgent_RegistersInAgentRegistry()
    {
        var agent = new AgentDefinition { Id = "coder", Provider = "openai", Model = "gpt-4o" };
        var services = new ServiceCollection();

        services.AddSpectra(s => s.AddAgent(agent));

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentRegistry>();

        Assert.Same(agent, registry.GetAgent("coder"));
    }

    [Fact]
    public void AddSpectra_WithMultipleAgents_RegistersAll()
    {
        var services = new ServiceCollection();

        services.AddSpectra(s =>
        {
            s.AddAgent(new AgentDefinition { Id = "coder", Provider = "openai", Model = "gpt-4o" });
            s.AddAgent(new AgentDefinition { Id = "reviewer", Provider = "anthropic", Model = "claude-sonnet" });
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentRegistry>();

        Assert.NotNull(registry.GetAgent("coder"));
        Assert.NotNull(registry.GetAgent("reviewer"));
        Assert.Equal(2, registry.GetAll().Count);
    }

    [Fact]
    public void AddSpectra_WithAgentBuilder_RegistersInAgentRegistry()
    {
        var services = new ServiceCollection();

        services.AddSpectra(s => s.AddAgent("coder", "openai", "gpt-4o", a =>
            a.WithTemperature(0.3).WithMaxTokens(4096)));

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentRegistry>();
        var agent = registry.GetAgent("coder");

        Assert.NotNull(agent);
        Assert.Equal("openai", agent!.Provider);
        Assert.Equal("gpt-4o", agent.Model);
        Assert.Equal(0.3, agent.Temperature);
        Assert.Equal(4096, agent.MaxTokens);
    }

    [Fact]
    public void AddSpectra_WithAgentsFromDirectory_RegistersAll()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"spectra_agent_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "coder.agent.json"),
                """{"id":"coder","provider":"openai","model":"gpt-4o"}""");
            File.WriteAllText(Path.Combine(dir, "reviewer.agent.json"),
                """{"id":"reviewer","provider":"anthropic","model":"claude-sonnet"}""");

            var services = new ServiceCollection();
            services.AddSpectra(s => s.AddAgentsFromDirectory(dir));

            var provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<IAgentRegistry>();

            Assert.NotNull(registry.GetAgent("coder"));
            Assert.NotNull(registry.GetAgent("reviewer"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AddSpectra_AddAgent_ThrowsOnNull()
    {
        var builder = new SpectraBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.AddAgent((AgentDefinition)null!));
    }

    // ── Fluent Return ─────────────────────────────────────────────

    [Fact]
    public void AddSpectra_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddSpectra(_ => { });

        Assert.Same(services, result);
    }

    // ── Test Doubles ─────────────────────────────────────────────

    private sealed record TestEvent : WorkflowEvent
    {
        [SetsRequiredMembers]
        public TestEvent()
        {
            RunId = "test";
            WorkflowId = "test";
            EventType = "test";
        }
    }

    private sealed class StubLlmProvider(string name) : ILlmProvider
    {
        public string Name => name;
        public bool SupportsModel(string model) => false;
        public ILlmClient CreateClient(AgentDefinition agent) => null!;
    }

    private sealed class StubTool(string name) : ITool
    {
        public string Name => name;
        public ToolDefinition Definition => new() { Name = name, Description = "stub" };
        public Task<ToolResult> ExecuteAsync(
            Dictionary<string, object?> arguments,
            WorkflowState state,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ToolResult { Content = "ok" });
    }

    private sealed class StubStep(string stepType) : IStep
    {
        public string StepType => stepType;
        public Task<StepResult> ExecuteAsync(StepContext context)
            => Task.FromResult(new StepResult { Status = StepStatus.Succeeded });
    }

    private sealed class StubPromptRegistry : IPromptRegistry
    {
        public PromptTemplate? GetPrompt(string name) => null;
        public IReadOnlyList<PromptTemplate> GetAll() => [];
        public void Register(PromptTemplate template) { }
        public void Reload() { }
    }

    private sealed class StubInterruptHandler : IInterruptHandler
    {
        public Task<InterruptResponse> HandleAsync(
            InterruptRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new InterruptResponse { Status = InterruptStatus.Approved });
    }

    private sealed class StubStateReducerRegistry : IStateReducerRegistry
    {
        public IStateReducer? Get(string key) => null;
        public void Register(IStateReducer reducer) { }
    }

    private sealed class StubStateMapper : IStateMapper
    {
        public Dictionary<string, object?> ResolveInputs(NodeDefinition node, WorkflowState state) => new();
        public void ApplyOutputs(NodeDefinition node, WorkflowState state, Dictionary<string, object?> outputs) { }
    }
}