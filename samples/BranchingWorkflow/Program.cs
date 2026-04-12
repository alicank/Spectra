using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Registration;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra =>
        {
            spectra.AddStep(new ClassifyStep());
            spectra.AddStep(new HandleStep());
            spectra.AddInMemoryCheckpoints();
            spectra.AddConsoleEvents();
        });
    })
    .Build();

var store = new JsonFileWorkflowStore("./workflows");
var workflow = store.Get("ticket-router")
    ?? throw new InvalidOperationException("Workflow 'ticket-router' not found.");

var runner = host.Services.GetRequiredService<IWorkflowRunner>();

// Try changing this to "normal", "low", or anything else to see different branches
var state = new WorkflowState();
state.Inputs["severity"] = args.Length > 0 ? args[0] : "critical";
state.Inputs["title"] = args.Length > 1 ? args[1] : "Database connection pool exhausted";

var result = await runner.RunAsync(workflow, state);

Console.WriteLine();
Console.WriteLine($"Errors: {result.Errors.Count}");
foreach (var e in result.Errors)
    Console.WriteLine($"  - {e}");

// ---------------------------------------------------------------------------
// ClassifyStep — reads severity from inputs and writes it as output.
// The conditional edges on the workflow read Context.classify.severity
// to decide which branch to take.
// ---------------------------------------------------------------------------
public class ClassifyStep : IStep
{
    public string StepType => "classify";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var severity = context.Inputs.TryGetValue("severity", out var s)
            ? s?.ToString()?.ToLowerInvariant() ?? "unknown"
            : "unknown";

        var title = context.Inputs.TryGetValue("title", out var t)
            ? t?.ToString() ?? ""
            : "";

        Console.WriteLine($"  [classify] Ticket: \"{title}\" → severity: {severity}");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?>
            {
                ["severity"] = severity,
                ["title"] = title
            }
        });
    }
}

// ---------------------------------------------------------------------------
// HandleStep — generic handler that prints which queue received the ticket.
// Used by all three branch nodes (escalate, process, backlog).
// ---------------------------------------------------------------------------
public class HandleStep : IStep
{
    public string StepType => "handle";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var queue = context.Inputs.TryGetValue("queue", out var q)
            ? q?.ToString() ?? "unknown"
            : "unknown";

        var title = context.Inputs.TryGetValue("title", out var t)
            ? t?.ToString() ?? ""
            : "";

        Console.WriteLine($"  [handle] → Routed to [{queue}] queue: \"{title}\"");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?>
            {
                ["queue"] = queue,
                ["handled"] = true
            }
        });
    }
}