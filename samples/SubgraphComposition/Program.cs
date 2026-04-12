// Spectra SubgraphComposition Sample
// Demonstrates child workflows (subgraphs) with isolated state and input/output mappings.
// A content publishing pipeline delegates SEO and social media generation to separate subgraphs.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Kernel.Execution;
using Spectra.Registration;
using Spectra.Workflow;
using System.Text.Json;
using Spectra.Contracts.Workflow;

// ── Sample article draft ─────────────────────────────────────────────────────

var defaultDraft = """
    Agentic AI frameworks are reshaping how enterprises build automation. Unlike traditional
    rule-based workflows, agentic systems let LLMs decide which tools to call, when to delegate
    to specialists, and how to recover from errors. This shift moves orchestration from rigid
    DAGs to adaptive, goal-driven pipelines. Early adopters in finance and healthcare report
    40% faster time-to-production for complex multi-step processes. The key enabler is composable
    architecture: small, testable workflow units (subgraphs) that snap together like building blocks,
    each with its own state isolation and clear data contracts.
    """;

var draft = args.Length > 0 ? string.Join(" ", args) : defaultDraft;

// ── Build host ───────────────────────────────────────────────────────────────

var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra =>
        {
            spectra.AddOpenRouter(config =>
            {
                config.ApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
                                ?? throw new InvalidOperationException("Set OPENROUTER_API_KEY environment variable.");
                config.Model = "openai/gpt-4o-mini";
            });
            spectra.AddInMemoryCheckpoints();
            spectra.AddConsoleEvents();
        });

        // Register SubgraphStep with deferred runner resolution to break circular dependency.
        // SubgraphStep needs IWorkflowRunner, but IWorkflowRunner needs IStepRegistry,
        // and IStepRegistry needs all steps including SubgraphStep → circular.
        // The fix: register SubgraphStep *after* the host is built, when all services are resolved.
        // We use a post-build hook pattern.
    })
    .Build();

// ── Post-build: register SubgraphStep ────────────────────────────────────────
// This breaks the circular dependency by resolving IWorkflowRunner after DI is fully built.

var stepRegistry = host.Services.GetRequiredService<IStepRegistry>();
var runner = host.Services.GetRequiredService<IWorkflowRunner>();
stepRegistry.Register(new SubgraphStep(runner));

// ── Load workflow ────────────────────────────────────────────────────────────

var workflowPath = Path.Combine(AppContext.BaseDirectory, "workflows", "content-publishing.workflow.json");
if (!File.Exists(workflowPath))
    workflowPath = Path.Combine(Directory.GetCurrentDirectory(), "workflows", "content-publishing.workflow.json");

var json = await File.ReadAllTextAsync(workflowPath);
var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(json, new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
})!;

// ── Display pipeline info ────────────────────────────────────────────────────

Console.WriteLine("═══ Content Publishing Pipeline ═══");
Console.WriteLine($"Draft: {draft[..Math.Min(80, draft.Length)].Trim()}...");
Console.WriteLine();
Console.WriteLine($"Loaded: {workflow.Name}");
Console.WriteLine($"  Main nodes: {workflow.Nodes.Count}");
Console.WriteLine($"  Subgraphs:  {workflow.Subgraphs.Count}");
foreach (var sg in workflow.Subgraphs)
{
    Console.WriteLine($"    └─ {sg.Id}: {sg.Workflow.Nodes.Count} nodes ({sg.Workflow.Name})");
    Console.WriteLine($"       Inputs:  {string.Join(", ", sg.InputMappings.Select(m => $"{m.Key} → {m.Value}"))}");
    Console.WriteLine($"       Outputs: {(sg.OutputMappings.Count > 0 ? string.Join(", ", sg.OutputMappings.Select(m => $"{m.Key} → {m.Value}")) : "(full child state)")}");
}
Console.WriteLine($"  Agents:     {workflow.Agents.Count}");
Console.WriteLine();

// ── Run ──────────────────────────────────────────────────────────────────────

var state = new WorkflowState();
state.Inputs["draft"] = draft;

Console.WriteLine("Running pipeline...");
Console.WriteLine(new string('─', 60));
Console.WriteLine();

var result = await runner.RunAsync(workflow, state);

Console.WriteLine();
Console.WriteLine(new string('─', 60));

// ── Display results ──────────────────────────────────────────────────────────

if (result.Errors.Count > 0)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nErrors ({result.Errors.Count}):");
    foreach (var err in result.Errors)
        Console.WriteLine($"  ✗ {err}");
    Console.ResetColor();
    return;
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("\n✓ Pipeline completed successfully\n");
Console.ResetColor();

// Show content analysis
if (result.Context.TryGetValue("analyze", out var analyzeRaw)
    && analyzeRaw is IDictionary<string, object?> analyzeCtx
    && analyzeCtx.TryGetValue("response", out var analysisResponse))
{
    Console.WriteLine("── Content Analysis ──");
    Console.WriteLine(analysisResponse);
    Console.WriteLine();
}

// Show SEO subgraph results (exposed as childContext since no explicit output mappings)
if (result.Context.TryGetValue("seo-optimization", out var seoRaw)
    && seoRaw is IDictionary<string, object?> seoCtx)
{
    Console.WriteLine("── SEO Subgraph Results ──");
    if (seoCtx.TryGetValue("childContext", out var childCtx) && childCtx is IDictionary<string, object?> seoChild)
    {
        foreach (var (key, value) in seoChild)
        {
            if (value is IDictionary<string, object?> nodeOutput && nodeOutput.TryGetValue("response", out var resp))
            {
                Console.WriteLine($"  [{key}]: {resp}");
                Console.WriteLine();
            }
        }
    }
}

// Show Social Media subgraph results
if (result.Context.TryGetValue("social-media-kit", out var socialRaw)
    && socialRaw is IDictionary<string, object?> socialCtx)
{
    Console.WriteLine("── Social Media Subgraph Results ──");
    if (socialCtx.TryGetValue("childContext", out var childCtx) && childCtx is IDictionary<string, object?> socialChild)
    {
        foreach (var (key, value) in socialChild)
        {
            if (value is IDictionary<string, object?> nodeOutput && nodeOutput.TryGetValue("response", out var resp))
            {
                Console.WriteLine($"  [{key}]: {resp}");
                Console.WriteLine();
            }
        }
    }
}

// Show final brief
if (result.Context.TryGetValue("compile-brief", out var briefRaw)
    && briefRaw is IDictionary<string, object?> briefCtx
    && briefCtx.TryGetValue("response", out var briefResponse))
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("══ Final Publishing Brief ══");
    Console.ResetColor();
    Console.WriteLine(briefResponse);
}