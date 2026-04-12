using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
using Spectra.Contracts.Workflow;
using Spectra.Registration;
using Spectra.Workflow;

var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
    ?? throw new InvalidOperationException(
        "Set OPENROUTER_API_KEY environment variable before running this sample.");

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra =>
        {
            spectra.AddOpenRouter(config =>
            {
                config.ApiKey = apiKey;
                config.Model = "openai/gpt-4o-mini";
            });
            spectra.AddInMemoryCheckpoints();
            spectra.AddConsoleEvents();
        });
    })
    .Build();

var runner = host.Services.GetRequiredService<IWorkflowRunner>();

// ── Path A: Code-first with WorkflowBuilder ──────────────────────────
Console.WriteLine("═══ PATH A: Code-first (WorkflowBuilder) ═══");
Console.WriteLine();

var codeWorkflow = WorkflowBuilder.Create("greet-and-translate")
    .WithName("Greet and Translate")
    .AddAgent("assistant", "openrouter", "openai/gpt-4o-mini", agent => agent
        .WithSystemPrompt("You are a helpful assistant. Keep responses short.")
        .WithMaxTokens(200))
    .AddAgentNode("greet", "assistant", node => node
        .WithUserPrompt("Say hello to {{inputs.name}} in one sentence.")
        .WithMaxIterations(1))
    .AddAgentNode("translate", "assistant", node => node
        .WithUserPrompt("Translate this to French: {{Context.greet.response}}")
        .WithMaxIterations(1))
    .AddEdge("greet", "translate")
    .Build();

var stateA = new WorkflowState();
stateA.Inputs["name"] = "Spectra";

var resultA = await runner.RunAsync(codeWorkflow, stateA);

Console.WriteLine();
PrintResult("A", resultA);

// ── Path B: JSON-first with JsonFileWorkflowStore ────────────────────
Console.WriteLine();
Console.WriteLine("═══ PATH B: JSON-first (workflow.json) ═══");
Console.WriteLine();

var store = new JsonFileWorkflowStore("./workflows");
var jsonWorkflow = store.Get("greet-and-translate")
    ?? throw new InvalidOperationException("Workflow 'greet-and-translate' not found.");

var stateB = new WorkflowState();
stateB.Inputs["name"] = "Spectra";

var resultB = await runner.RunAsync(jsonWorkflow, stateB);

Console.WriteLine();
PrintResult("B", resultB);

// ── Compare ──────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("═══ COMPARISON ═══");
Console.WriteLine();
Console.WriteLine($"  Both workflows have the same structure:");
Console.WriteLine($"  Code nodes: {codeWorkflow.Nodes.Count}, JSON nodes: {jsonWorkflow.Nodes.Count}");
Console.WriteLine($"  Code edges: {codeWorkflow.Edges.Count}, JSON edges: {jsonWorkflow.Edges.Count}");
Console.WriteLine($"  Code agents: {codeWorkflow.Agents.Count}, JSON agents: {jsonWorkflow.Agents.Count}");
Console.WriteLine($"  Both produced output: {resultA.Errors.Count == 0 && resultB.Errors.Count == 0}");

static void PrintResult(string label, WorkflowState result)
{
    Console.WriteLine($"── Result {label} ──────────────────────────────────────────");

    if (result.Context.TryGetValue("greet", out var greetObj)
        && greetObj is IDictionary<string, object?> greetDict
        && greetDict.TryGetValue("response", out var greetResponse))
    {
        Console.WriteLine($"  Greeting  : {greetResponse}");
    }

    if (result.Context.TryGetValue("translate", out var transObj)
        && transObj is IDictionary<string, object?> transDict
        && transDict.TryGetValue("response", out var transResponse))
    {
        Console.WriteLine($"  Translated: {transResponse}");
    }

    Console.WriteLine($"  Errors    : {result.Errors.Count}");
}