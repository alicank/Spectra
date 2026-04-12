// Spectra AgentWithFallback Sample
// Demonstrates resilient multi-provider LLM routing with two fallback strategies:
//   1. RoundRobin — load-balances classification across Anthropic models
//   2. Failover  — falls back from Anthropic to OpenRouter with a quality gate

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Providers.Fallback;
using Spectra.Contracts.State;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Resilience;
using Spectra.Registration;

var anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
    ?? throw new InvalidOperationException(
        "Set ANTHROPIC_API_KEY environment variable.");

var openRouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
    ?? throw new InvalidOperationException(
        "Set OPENROUTER_API_KEY environment variable.");

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra =>
        {
            // ── Providers ──
            spectra.AddAnthropic(c =>
            {
                c.ApiKey = anthropicKey;
            });

            spectra.AddOpenRouter(c =>
            {
                c.ApiKey = openRouterKey;
            });

            // ── Fallback Policy 1: RoundRobin ──
            // Load-balances the classify node across two Anthropic models.
            // Each request rotates the starting model; on failure, the other is tried.
            spectra.AddFallbackPolicy("load-balanced",
                strategy: FallbackStrategy.RoundRobin,
                entries:
                [
                    new FallbackProviderEntry
                    {
                        Provider = "anthropic",
                        Model = "claude-sonnet-4-20250514"
                    },
                    new FallbackProviderEntry
                    {
                        Provider = "anthropic",
                        Model = "claude-haiku-4-5-20251001"
                    }
                ]);

            // ── Fallback Policy 2: Failover with quality gate ──
            // The summarize node tries Anthropic first; if it fails or the response
            // is too short (quality gate), it falls back to OpenRouter.
            spectra.AddFallbackPolicy("failover-chain",
                strategy: FallbackStrategy.Failover,
                entries:
                [
                    new FallbackProviderEntry
                    {
                        Provider = "anthropic",
                        Model = "claude-sonnet-4-20250514"
                    },
                    new FallbackProviderEntry
                    {
                        Provider = "openrouter",
                        Model = "openai/gpt-4o-mini"
                    }
                ],
                defaultQualityGate: new MinLengthQualityGate(50));

            spectra.AddInMemoryCheckpoints();
            spectra.AddConsoleEvents();
        });
    })
    .Build();

// ── Load workflow from JSON ──
var store = new JsonFileWorkflowStore("./workflows");
var workflow = store.Get("incident-response")
    ?? throw new InvalidOperationException("Workflow 'incident-response' not found.");

Console.WriteLine($"Loaded: {workflow.Name} ({workflow.Nodes.Count} nodes)");
Console.WriteLine();

// ── Prepare state ──
var state = new WorkflowState();
state.Inputs["report"] = args.Length > 0
    ? string.Join(" ", args)
    : """
      At 03:47 UTC the primary PostgreSQL replica in us-east-1 stopped replicating.
      Replication lag exceeded 120 seconds and continued climbing. The connection pool
      on the API tier began saturating at 03:52, causing HTTP 503 responses for 12% of
      requests to the /api/orders endpoint. Auto-scaling added two read replicas but
      they could not catch up. The on-call DBA manually failed over to the standby at
      04:15, restoring service by 04:22. Root cause appears to be a long-running
      analytical query that acquired row-level locks across the orders table.
      """;

// ── Run ──
var runner = host.Services.GetRequiredService<IWorkflowRunner>();
var result = await runner.RunAsync(workflow, state);

// ── Output ──
Console.WriteLine();
Console.WriteLine("═══ INCIDENT RESPONSE ═══");

if (result.Context.TryGetValue("classify", out var classifyObj)
    && classifyObj is Dictionary<string, object?> classifyDict
    && classifyDict.TryGetValue("response", out var severity))
{
    Console.WriteLine($"  Severity : {severity}");

    if (classifyDict.TryGetValue("model", out var classifyModel))
        Console.WriteLine($"  Model    : {classifyModel}");
}

if (result.Context.TryGetValue("summarize", out var summarizeObj)
    && summarizeObj is Dictionary<string, object?> summarizeDict
    && summarizeDict.TryGetValue("response", out var summary))
{
    Console.WriteLine();
    Console.WriteLine("  Summary:");
    Console.WriteLine($"  {summary}");

    if (summarizeDict.TryGetValue("model", out var summarizeModel))
        Console.WriteLine($"\n  Model    : {summarizeModel}");
}

Console.WriteLine();
Console.WriteLine($"Errors: {result.Errors.Count}");
foreach (var e in result.Errors)
    Console.WriteLine($"  - {e}");