using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Scheduling;
using Spectra.Registration;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra =>
        {
            spectra.AddStep(new AnalyzeStep());
            spectra.AddStep(new MergeStep());
            spectra.AddInMemoryCheckpoints();
            spectra.AddConsoleEvents();
        });
    })
    .Build();

var store = new JsonFileWorkflowStore("./workflows");
var workflow = store.Get("parallel-analysis")
    ?? throw new InvalidOperationException("Workflow 'parallel-analysis' not found.");

// ParallelScheduler handles fan-out/fan-in — not the sequential WorkflowRunner
var scheduler = host.Services.GetRequiredService<ParallelScheduler>();

var state = new WorkflowState();
state.Inputs["document"] = "Spectra is a .NET framework for AI workflow orchestration.";

var result = await scheduler.ExecuteAsync(workflow, state);

Console.WriteLine();
Console.WriteLine("── Results ─────────────────────────────────────────────");
if (result.Context.TryGetValue("merge", out var mergeOutput) && mergeOutput is IDictionary<string, object?> outputs)
{
    outputs.TryGetValue("sentiment", out var sentimentVal);
    outputs.TryGetValue("entities", out var entitiesVal);
    outputs.TryGetValue("mergedAt", out var mergedAtVal);
    Console.WriteLine($"  Sentiment : {sentimentVal}");
    Console.WriteLine($"  Entities  : {entitiesVal}");
    Console.WriteLine($"  Merged at : {mergedAtVal}");
}
Console.WriteLine($"  Errors    : {result.Errors.Count}");

// ---------------------------------------------------------------------------
// AnalyzeStep — simulates an analysis task with a 500ms delay.
// Two instances run in parallel (sentiment + entities).
// The delay proves they execute concurrently, not sequentially.
// ---------------------------------------------------------------------------
public class AnalyzeStep : IStep
{
    public string StepType => "analyze";

    public async Task<StepResult> ExecuteAsync(StepContext context)
    {
        var task = context.Inputs.TryGetValue("task", out var t)
            ? t?.ToString() ?? "unknown"
            : "unknown";

        var document = context.Inputs.TryGetValue("document", out var d)
            ? d?.ToString() ?? ""
            : "";

        Console.WriteLine($"  [analyze:{context.NodeId}] Starting '{task}' ...");

        // Simulate work — both branches run this delay concurrently
        await Task.Delay(500, context.CancellationToken);

        var result = task switch
        {
            "sentiment" => $"positive (confidence: 0.87)",
            "entities" => $"[Spectra, .NET, AI]",
            _ => $"done: {task}"
        };

        Console.WriteLine($"  [analyze:{context.NodeId}] Completed '{task}' → {result}");

        return new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?> { [task] = result }
        };
    }
}

// ---------------------------------------------------------------------------
// MergeStep — collects outputs from both parallel branches.
// Only runs after both branches complete (WaitForAll = true in JSON).
// ---------------------------------------------------------------------------
public class MergeStep : IStep
{
    public string StepType => "merge";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var sentiment = context.State.Context.TryGetValue("sentiment", out var s)
            && s is IDictionary<string, object?> sd
            ? (sd.TryGetValue("sentiment", out var sv) ? sv?.ToString() ?? "n/a" : "n/a")
            : "n/a";

        var entities = context.State.Context.TryGetValue("entities", out var e)
            && e is IDictionary<string, object?> ed
            ? (ed.TryGetValue("entities", out var ev) ? ev?.ToString() ?? "n/a" : "n/a")
            : "n/a";

        Console.WriteLine($"  [merge] Combining: sentiment={sentiment}, entities={entities}");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?>
            {
                ["sentiment"] = sentiment,
                ["entities"] = entities,
                ["mergedAt"] = DateTimeOffset.UtcNow.ToString("o")
            }
        });
    }
}