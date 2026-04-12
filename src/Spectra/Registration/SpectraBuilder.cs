using Spectra.Contracts.Threading;
using Spectra.Contracts.Audit;
using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.Mcp;
using Spectra.Contracts.Memory;
using Spectra.Contracts.Prompts;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Providers.Fallback;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Streaming;
using Spectra.Contracts.Tools;
using System.Reflection;
using Spectra.Contracts.Workflow;
using Spectra.Extensions.Checkpointing;
using Spectra.Kernel.Prompts;
using Spectra.Kernel.Execution;

namespace Spectra.Registration;

/// <summary>
/// Fluent builder surfaced to the consumer in the AddSpectra callback.
/// Collects provider registrations that are later pushed into the IProviderRegistry.
public sealed class SpectraBuilder
{
    internal List<ILlmProvider> Providers { get; } = [];
    internal List<ITool> Tools { get; } = [];
    internal List<Assembly> ToolAssemblies { get; } = [];
    internal List<McpServerConfig> McpServers { get; } = [];
    internal List<AgentDefinition> Agents { get; } = [];

    internal List<Func<IServiceProvider, IStep>> StepFactories { get; } = [];
    internal IMemoryStore? MemoryStore { get; private set; }
    internal MemoryOptions MemoryOptions { get; private set; } = MemoryOptions.Default;

    internal ICheckpointStore? CheckpointStore { get; private set; }
    internal CheckpointOptions CheckpointOptions { get; private set; } = new();
    internal List<IEventSink> EventSinks { get; } = [];
    internal IPromptRegistry? PromptRegistry { get; private set; }
    internal IInterruptHandler? InterruptHandler { get; private set; }
    internal IWorkflowStore? WorkflowStore { get; private set; }
    internal IStateReducerRegistry? StateReducerRegistry { get; private set; }
    internal IAuditStore? AuditStore { get; private set; }
    internal IThreadManager? ThreadManager { get; private set; }
    internal List<IFallbackPolicy> FallbackPolicies { get; } = [];
    internal ToolResilienceOptions? ToolResilienceOptions { get; private set; }

    /// <summary>
    /// Registers a global agent definition into the agent catalog.
    /// Workflows can reference this agent by its ID without redefining it.
    /// </summary>
    public SpectraBuilder AddAgent(AgentDefinition agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        Agents.Add(agent);
        return this;
    }

    /// <summary>
    /// Registers a global agent definition using a fluent builder.
    /// </summary>
    public SpectraBuilder AddAgent(string id, string provider, string model,
        Action<Spectra.Workflow.AgentBuilder>? configure = null)
    {
        var agentBuilder = new Spectra.Workflow.AgentBuilder(id, provider, model);
        configure?.Invoke(agentBuilder);
        Agents.Add(agentBuilder.Build());
        return this;
    }

    /// <summary>
    /// Loads all .agent.json files from a directory and registers them as global agents.
    /// </summary>
    public SpectraBuilder AddAgentsFromDirectory(string directory)
    {
        var agents = Spectra.Kernel.Execution.FileAgentLoader.LoadFromDirectory(directory);
        Agents.AddRange(agents);
        return this;
    }

    internal int MaxParallelism { get; private set; } = 4;

    /// <summary>
    /// Registers a long-term memory store for cross-session persistence.
    /// </summary>
    public SpectraBuilder AddMemory(IMemoryStore store, Action<MemoryOptions>? configure = null)
    {
        MemoryStore = store;
        if (configure is not null)
            configure(MemoryOptions);
        return this;
    }

    /// <summary>
    /// Registers a custom step instance.
    /// </summary>
    public SpectraBuilder AddStep(IStep step)
    {
        StepFactories.Add(_ => step);
        return this;
    }

    /// <summary>
    /// Registers a step using a factory that receives the <see cref="IServiceProvider"/>.
    /// Useful for steps that require injected dependencies (e.g. SubgraphStep needs IWorkflowRunner).
    /// </summary>
    public SpectraBuilder AddStep(Func<IServiceProvider, IStep> factory)
    {
        StepFactories.Add(factory);
        return this;
    }

    /// <summary>
    /// Registers a step by type. The step will be resolved from the service provider.
    /// </summary>
    public SpectraBuilder AddStep<TStep>() where TStep : class, IStep
    {
        StepFactories.Add(sp => (IStep)sp.GetService(typeof(TStep))!);
        return this;
    }

    /// <summary>
    /// Registers an in-memory memory store for development and testing.
    /// </summary>
    public SpectraBuilder AddInMemoryMemory(Action<MemoryOptions>? configure = null)
        => AddMemory(new InMemoryMemoryStore(), configure);

    /// <summary>
    /// Registers a file-backed memory store for local development.
    /// </summary>
    public SpectraBuilder AddFileMemory(string directory, Action<MemoryOptions>? configure = null)
        => AddMemory(new FileMemoryStore(directory), configure);

    /// <summary>
    /// Registers a custom checkpoint store for workflow state persistence.
    /// </summary>
    public SpectraBuilder AddCheckpoints(ICheckpointStore store, Action<CheckpointOptions>? configure = null)
    {
        CheckpointStore = store;
        if (configure is not null)
            configure(CheckpointOptions);
        return this;
    }

    /// <summary>
    /// Registers an in-memory checkpoint store for development and testing.
    /// </summary>
    public SpectraBuilder AddInMemoryCheckpoints(Action<CheckpointOptions>? configure = null)
        => AddCheckpoints(new InMemoryCheckpointStore(), configure);

    /// <summary>
    /// Registers a file-backed checkpoint store for local development.
    /// </summary>
    public SpectraBuilder AddFileCheckpoints(string directory, Action<CheckpointOptions>? configure = null)
        => AddCheckpoints(new FileCheckpointStore(directory), configure);

    /// <summary>
    /// Registers an event sink for workflow diagnostics and observability.
    /// </summary>
    public SpectraBuilder AddEventSink(IEventSink sink)
    {
        EventSinks.Add(sink);
        return this;
    }

    /// <summary>
    /// Registers an audit store for compliance audit trail.
    /// The audit sink is automatically added to the event pipeline.
    /// </summary>
    public SpectraBuilder AddAuditStore(IAuditStore store)
    {
        AuditStore = store;
        return this;
    }

    /// <summary>
    /// Registers an in-memory audit store for development and testing.
    /// </summary>
    public SpectraBuilder AddInMemoryAudit()
        => AddAuditStore(new Kernel.Audit.InMemoryAuditStore());

    /// <summary>
    /// Registers the console event sink for development logging.
    /// </summary>
    public SpectraBuilder AddConsoleEvents()
        => AddEventSink(new ConsoleEventSink());

    /// <summary>
    /// Registers a prompt registry for template-based prompts.
    /// </summary>
    public SpectraBuilder AddPrompts(IPromptRegistry registry)
    {
        PromptRegistry = registry;
        return this;
    }

    /// <summary>
    /// Registers a file-backed prompt registry loading templates from a directory.
    /// </summary>
    public SpectraBuilder AddPromptsFromDirectory(string directory)
        => AddPrompts(new FilePromptRegistry(directory));

    /// <summary>
    /// Registers an interrupt handler for human-in-the-loop workflows.
    /// </summary>
    public SpectraBuilder AddInterruptHandler(IInterruptHandler handler)
    {
        InterruptHandler = handler;
        return this;
    }

    /// <summary>
    /// Registers a state reducer registry for custom state reduction logic.
    /// </summary>
    public SpectraBuilder AddStateReducers(IStateReducerRegistry registry)
    {
        StateReducerRegistry = registry;
        return this;
    }

    /// <summary>
    /// Configures the maximum degree of parallelism for parallel node execution.
    /// </summary>
    public SpectraBuilder ConfigureParallelism(int maxDegreeOfParallelism)
    {
        MaxParallelism = maxDegreeOfParallelism;
        return this;
    }

    /// <summary>
    /// Registers a workflow store for loading workflow definitions.
    /// </summary>
    public SpectraBuilder AddWorkflows(IWorkflowStore store)
    {
        WorkflowStore = store;
        return this;
    }

    /// <summary>
    /// Registers a file-backed workflow store loading JSON definitions from a directory.
    /// </summary>
    public SpectraBuilder AddWorkflowsFromDirectory(string directory, string searchPattern = "*.workflow.json")
        => AddWorkflows(new JsonFileWorkflowStore(directory, searchPattern));

    /// <summary>
    /// Registers a single tool instance.
    /// </summary>
    public SpectraBuilder AddTool(ITool tool)
    {
        Tools.Add(tool);
        return this;
    }

    /// <summary>
    /// Registers multiple tool instances.
    /// </summary>
    public SpectraBuilder AddTools(params ITool[] tools)
    {
        Tools.AddRange(tools);
        return this;
    }

    /// <summary>
    /// Registers an assembly for tool auto-discovery.
    /// All classes implementing <see cref="ITool"/> and decorated with
    /// <see cref="SpectraToolAttribute"/> will be discovered and registered.
    /// </summary>
    public SpectraBuilder AddToolsFromAssembly(Assembly assembly)
    {
        ToolAssemblies.Add(assembly);
        return this;
    }

    /// <summary>
    /// Registers the assembly containing <typeparamref name="T"/> for tool auto-discovery.
    /// </summary>
    public SpectraBuilder AddToolsFromAssembly<T>()
        => AddToolsFromAssembly(typeof(T).Assembly);

    /// <summary>
    /// Registers an MCP server. Tools discovered from this server
    /// are adapted as native <see cref="ITool"/> instances and added to the tool registry.
    /// </summary>
    public SpectraBuilder AddMcpServer(McpServerConfig config)
    {
        McpServers.Add(config);
        return this;
    }

    /// <summary>
    /// Registers an MCP server using a fluent configuration callback.
    /// </summary>
    public SpectraBuilder AddMcpServer(string name, Action<McpServerConfigBuilder> configure)
    {
        var builder = new McpServerConfigBuilder(name);
        configure(builder);
        McpServers.Add(builder.Build());
        return this;
    }


    /// <summary>
    /// Registers a custom thread manager for thread lifecycle management.
    /// </summary>
    public SpectraBuilder AddThreadManager(IThreadManager threadManager)
    {
        ThreadManager = threadManager;
        return this;
    }

    /// <summary>
    /// Registers an in-memory thread manager for development and testing.
    /// </summary>
    public SpectraBuilder AddInMemoryThreadManager()
    {
        // Deferred — actual wiring happens in ServiceCollectionExtensions
        // so the checkpoint store dependency can be resolved from DI.
        ThreadManager = null; // sentinel: InMemory requested
        _useInMemoryThreadManager = true;
        return this;
    }

    internal bool _useInMemoryThreadManager;

    /// <summary>
    /// Registers a named fallback policy for provider-level failover.
    /// </summary>
    public SpectraBuilder AddFallbackPolicy(IFallbackPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        FallbackPolicies.Add(policy);
        return this;
    }

    /// <summary>
    /// Registers a named fallback policy using inline configuration.
    /// <code>
    /// builder.AddFallbackPolicy("resilient-gpt", strategy: FallbackStrategy.Failover, entries: new[]
    /// {
    ///     new FallbackProviderEntry { Provider = "openai", Model = "gpt-4o" },
    ///     new FallbackProviderEntry { Provider = "anthropic", Model = "claude-sonnet-4-20250514" },
    ///     new FallbackProviderEntry { Provider = "ollama", Model = "llama3" }
    /// });
    /// </code>
    /// </summary>
    public SpectraBuilder AddFallbackPolicy(
        string name,
        FallbackStrategy strategy,
        IEnumerable<FallbackProviderEntry> entries,
        IQualityGate? defaultQualityGate = null)
    {
        return AddFallbackPolicy(new FallbackPolicy
        {
            Name = name,
            Strategy = strategy,
            Entries = entries.ToList(),
            DefaultQualityGate = defaultQualityGate
        });
    }

    /// <summary>
    /// Enables per-tool circuit breaker resilience.
    /// When individual tools fail repeatedly, their circuit opens and calls are
    /// either routed to a fallback or rejected — preventing cascading failures.
    /// <code>
    /// builder.AddToolResilience(opts =>
    /// {
    ///     opts.FailureThreshold = 3;
    ///     opts.CooldownPeriod = TimeSpan.FromSeconds(30);
    ///     opts.FallbackTools["mcp:weather-api:get_forecast"] = "mcp:backup-weather:get_forecast";
    /// });
    /// </code>
    /// </summary>
    public SpectraBuilder AddToolResilience(Action<ToolResilienceOptions>? configure = null)
    {
        var options = new ToolResilienceOptions();
        configure?.Invoke(options);
        ToolResilienceOptions = options;
        return this;
    }

    internal void AddProvider(ILlmProvider provider) => Providers.Add(provider);
}

