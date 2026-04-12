// Spectra MemoryStoreRecall Sample
// Demonstrates long-term memory: store a fact in one node, recall it in another.
// No API key needed — this is pure engine with InMemoryMemoryStore.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
using Spectra.Registration;
using Spectra.Workflow;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra =>
        {
            // In-memory memory store — entries live for the process lifetime.
            // For production, swap with FileMemoryStore or a custom IMemoryStore.
            spectra.AddInMemoryMemory();

            spectra.AddInMemoryCheckpoints();
            spectra.AddConsoleEvents();
        });
    })
    .Build();

// ── Workflow: store a user preference, then recall it ────────────────────────

var workflow = WorkflowBuilder.Create("memory-demo")
    .WithName("Memory Store & Recall")
    .AddNode("store", "memory.store", node => node
        .WithParameter("namespace", "user-preferences")
        .WithParameter("key", "favorite-language")
        .WithParameter("content", "{{inputs.language}}")
        .WithParameter("tags", "preferences,onboarding"))
    .AddNode("recall", "memory.recall", node => node
        .WithParameter("namespace", "user-preferences")
        .WithParameter("key", "favorite-language"))
    .AddEdge("store", "recall")
    .Build();

// ── Run ──────────────────────────────────────────────────────────────────────

var runner = host.Services.GetRequiredService<IWorkflowRunner>();

var state = new WorkflowState();
state.Inputs["language"] = args.Length > 0 ? args[0] : "C#";

Console.WriteLine($"Storing preference: language = \"{state.Inputs["language"]}\"");
Console.WriteLine();

var result = await runner.RunAsync(workflow, state);

Console.WriteLine();

// ── Read outputs ─────────────────────────────────────────────────────────────

if (result.Context.TryGetValue("store", out var storeRaw)
    && storeRaw is IDictionary<string, object?> storeOut)
{
    storeOut.TryGetValue("stored", out var stored);
    storeOut.TryGetValue("action", out var action);
    storeOut.TryGetValue("key", out var key);
    Console.WriteLine($"Store  → stored: {stored}, action: {action}, key: {key}");
}

if (result.Context.TryGetValue("recall", out var recallRaw)
    && recallRaw is IDictionary<string, object?> recallOut)
{
    recallOut.TryGetValue("found", out var found);
    recallOut.TryGetValue("count", out var count);
    recallOut.TryGetValue("memories", out var memories);
    Console.WriteLine($"Recall → found: {found}, count: {count}");

    if (memories is IEnumerable<object> list)
    {
        foreach (var entry in list)
        {
            if (entry is IDictionary<string, object?> mem)
            {
                mem.TryGetValue("Key", out var k);
                mem.TryGetValue("Content", out var c);
                mem.TryGetValue("Namespace", out var ns);
                Console.WriteLine($"         namespace: {ns}, key: {k}, content: \"{c}\"");
            }
        }
    }
}

Console.WriteLine($"\nErrors: {result.Errors.Count}");
foreach (var e in result.Errors)
    Console.WriteLine($"  - {e}");