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
            spectra.AddStep(new EchoStep());
            spectra.AddInMemoryCheckpoints();
            spectra.AddConsoleEvents();
        });
    })
    .Build();

var store = new JsonFileWorkflowStore("./workflows");
var workflow = store.Get("hello-world")
               ?? throw new InvalidOperationException("Workflow 'hello-world' not found.");

var runner = host.Services.GetRequiredService<IWorkflowRunner>();

var state = new WorkflowState();
state.Inputs["name"] = "World";

var result = await runner.RunAsync(workflow, state);

Console.WriteLine();
Console.WriteLine($"Errors: {result.Errors.Count}");
foreach (var e in result.Errors)
    Console.WriteLine($"  - {e}");

// ---------------------------------------------------------------------------
// EchoStep — a minimal IStep that reads "message" from parameters and
// writes it back as output. No LLM, no API key, just the engine.
// ---------------------------------------------------------------------------
public class EchoStep : IStep
{
    public string StepType => "echo";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var message = context.Inputs.TryGetValue("message", out var raw)
            ? raw?.ToString() ?? "(null)"
            : "(no message)";

        Console.WriteLine($"  [echo] {message}");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?> { ["message"] = message }
        });
    }
}