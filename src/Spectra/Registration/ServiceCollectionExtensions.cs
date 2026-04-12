using Spectra.Contracts.Threading;
using Spectra.Kernel.Threading;
using Spectra.Contracts.Audit;
using Spectra.Kernel.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Evaluation;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.Memory;
using Spectra.Contracts.Prompts;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Providers.Fallback;
using Spectra.Kernel.Resilience;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Tools;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Evaluation;
using Spectra.Kernel.Execution;
using Spectra.Kernel.Mcp;
using Spectra.Kernel.Prompts;
using Spectra.Kernel.Scheduling;

namespace Spectra.Registration;

/// <summary>
/// Extension methods for registering Spectra into an <see cref="IServiceCollection"/>.
/// Follows the standard Microsoft DI pattern used by AddDbContext, AddMediatR, etc.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all Spectra services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">A callback to configure Spectra via <see cref="SpectraBuilder"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSpectra(
        this IServiceCollection services,
        Action<SpectraBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new SpectraBuilder();
        configure(builder);

        // --- Registries ---

        // Step registry: built-in steps + user-registered steps (factory-based deferred to IServiceProvider)
        services.TryAddSingleton<IStepRegistry>(sp =>
        {
            var registry = new InMemoryStepRegistry();
            foreach (var factory in builder.StepFactories)
            {
                var step = factory(sp);
                registry.Register(step);
            }
            return registry;
        });

        // Tool registry: explicit tools registered synchronously.
        // Assembly-discovered tools and MCP tools are added by the hosted service.
        // If tool resilience is enabled, tools are wrapped with circuit breaker decorators.
        services.TryAddSingleton<IToolRegistry>(sp =>
        {
            var registry = new InMemoryToolRegistry();

            if (builder.ToolResilienceOptions is not null)
            {
                var policy = new DefaultToolResiliencePolicy(builder.ToolResilienceOptions);
                var eventSink = sp.GetRequiredService<IEventSink>();

                foreach (var tool in builder.Tools)
                {
                    var decorated = new ResilientToolDecorator(tool, policy, registry, eventSink);
                    registry.Register(decorated);
                }

                // Store policy and event sink for the hosted service to wrap MCP/assembly tools
                registry.SetResiliencePolicy(policy, eventSink);
            }
            else
            {
                foreach (var tool in builder.Tools)
                    registry.Register(tool);
            }

            return registry;
        });

        // Tool resilience policy: register as singleton if configured
        if (builder.ToolResilienceOptions is not null)
        {
            services.TryAddSingleton<IToolResiliencePolicy>(sp =>
            {
                var registry = sp.GetRequiredService<IToolRegistry>();
                if (registry is InMemoryToolRegistry inMemory && inMemory.ResiliencePolicy is not null)
                    return inMemory.ResiliencePolicy;
                return new DefaultToolResiliencePolicy(builder.ToolResilienceOptions);
            });
        }

        // Provider registry
        services.TryAddSingleton<IProviderRegistry>(_ =>
        {
            var registry = new InMemoryProviderRegistry();
            foreach (var provider in builder.Providers)
                registry.Register(provider);
            return registry;
        });

        // Agent registry
        services.TryAddSingleton<IAgentRegistry>(_ =>
        {
            var registry = new InMemoryAgentRegistry();
            foreach (var agent in builder.Agents)
                registry.Register(agent);
            return registry;
        });

        // Fallback policy registry
        services.TryAddSingleton<IFallbackPolicyRegistry>(_ =>
        {
            var registry = new InMemoryFallbackPolicyRegistry();
            foreach (var policy in builder.FallbackPolicies)
                registry.Register(policy);
            return registry;
        });

        // --- Core services ---

        services.TryAddSingleton<IStateMapper>(_ => new StateMapper());
        services.TryAddSingleton<IConditionEvaluator>(_ => new SimpleConditionEvaluator());

        // Event sink: composite from all configured sinks, or null-object
        services.TryAddSingleton<IEventSink>(_ =>
        {
            if (builder.EventSinks.Count == 0)
                return NullEventSink.Instance;

            if (builder.EventSinks.Count == 1)
                return builder.EventSinks[0];

            return new CompositeEventSink(builder.EventSinks);
        });

        // --- Optional services ---

        if (builder.CheckpointStore is not null)
            services.TryAddSingleton<ICheckpointStore>(builder.CheckpointStore);

        services.TryAddSingleton(builder.CheckpointOptions);

        if (builder.MemoryStore is not null)
            services.TryAddSingleton<IMemoryStore>(builder.MemoryStore);

        // Prompt registry: always register (file-backed if configured, in-memory default otherwise)
        if (builder.PromptRegistry is not null)
            services.TryAddSingleton<IPromptRegistry>(builder.PromptRegistry);
        else
            services.TryAddSingleton<IPromptRegistry>(_ => new InMemoryPromptRegistry());

        // Prompt renderer: stateless singleton
        services.TryAddSingleton<PromptRenderer>();

        if (builder.InterruptHandler is not null)
            services.TryAddSingleton<IInterruptHandler>(builder.InterruptHandler);

        if (builder.StateReducerRegistry is not null)
            services.TryAddSingleton<IStateReducerRegistry>(builder.StateReducerRegistry);

        // Audit store: if configured, register the store and inject an AuditEventSink
        if (builder.AuditStore is not null)
        {
            services.TryAddSingleton<IAuditStore>(builder.AuditStore);
            builder.EventSinks.Add(new AuditEventSink(
                builder.AuditStore,
                () => RunContext.Anonymous)); // Default; overridden per-run by WorkflowRunner
        }

        if (builder.WorkflowStore is not null)
            services.TryAddSingleton<IWorkflowStore>(builder.WorkflowStore);

        // Thread manager
        if (builder.ThreadManager is not null)
        {
            services.TryAddSingleton<IThreadManager>(builder.ThreadManager);
        }
        else if (builder._useInMemoryThreadManager)
        {
            services.TryAddSingleton<IThreadManager>(sp =>
                new InMemoryThreadManager(sp.GetService<ICheckpointStore>()));
        }

        // --- Built-in steps ---
        // Auto-register all Spectra built-in step types.
        // Inserted at the front so user-registered steps can override if needed.

        builder.StepFactories.Insert(0, sp => new PromptStep(
            sp.GetRequiredService<IProviderRegistry>(),
            sp.GetRequiredService<IAgentRegistry>(),
            sp.GetRequiredService<PromptRenderer>(),
            sp.GetService<IPromptRegistry>(),
            sp.GetService<IFallbackPolicyRegistry>(),
            sp.GetService<IEventSink>()));

        builder.StepFactories.Insert(1, sp => new StructuredOutputStep(
            sp.GetRequiredService<IProviderRegistry>(),
            sp.GetRequiredService<IAgentRegistry>(),
            sp.GetRequiredService<PromptRenderer>(),
            sp.GetService<IPromptRegistry>(),
            sp.GetService<IFallbackPolicyRegistry>(),
            sp.GetService<IEventSink>()));

        builder.StepFactories.Insert(2, sp => new AgentStep(
            sp.GetRequiredService<IProviderRegistry>(),
            sp.GetRequiredService<IAgentRegistry>(),
            sp.GetRequiredService<IToolRegistry>(),
            sp.GetRequiredService<PromptRenderer>(),
            sp.GetService<IPromptRegistry>(),
            sp.GetService<IEventSink>(),
            sp.GetService<IMemoryStore>(),
            null,
            sp.GetService<IFallbackPolicyRegistry>()));

        builder.StepFactories.Insert(3, sp => new SessionStep(
            sp.GetRequiredService<IProviderRegistry>(),
            sp.GetRequiredService<IAgentRegistry>(),
            sp.GetRequiredService<IToolRegistry>(),
            sp.GetRequiredService<PromptRenderer>(),
            sp.GetService<IPromptRegistry>(),
            sp.GetService<IEventSink>()));

        builder.StepFactories.Insert(4, _ => new MemoryStoreStep());
        builder.StepFactories.Insert(5, _ => new MemoryRecallStep());

        // --- Workflow execution ---

        services.TryAddSingleton<IWorkflowRunner>(sp => new WorkflowRunner(
            sp.GetRequiredService<IStepRegistry>(),
            sp.GetRequiredService<IStateMapper>(),
            sp.GetRequiredService<IConditionEvaluator>(),
            sp.GetRequiredService<IEventSink>(),
            sp.GetService<ICheckpointStore>(),
            sp.GetService<CheckpointOptions>(),
            sp.GetService<IAgentRegistry>(),
            sp.GetService<IProviderRegistry>(),
            sp.GetService<IInterruptHandler>(),
            sp,
            sp.GetService<IMemoryStore>()));

        services.TryAddSingleton(sp => new ParallelScheduler(
            sp.GetRequiredService<IStepRegistry>(),
            sp.GetRequiredService<IEventSink>(),
            sp.GetService<ICheckpointStore>(),
            sp.GetRequiredService<IConditionEvaluator>(),
            sp,
            sp.GetService<IInterruptHandler>()));

        // Always register the hosted service — needed for tool discovery, MCP, and agent tool validation
        services.AddSingleton(sp => new SpectraHostedService(
            builder.McpServers,
            builder.ToolAssemblies,
            sp.GetRequiredService<IToolRegistry>(),
            sp.GetRequiredService<IEventSink>(),
            sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<SpectraHostedService>(),
            sp.GetService<IAgentRegistry>()));


        services.AddHostedService(sp => sp.GetRequiredService<SpectraHostedService>());


        return services;
    }
}