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
            spectra.AddStep(new AttemptStep());
            spectra.AddStep(new CheckStep());
            spectra.AddStep(new DoneStep());
            spectra.AddInMemoryCheckpoints();
            spectra.AddConsoleEvents();
        });
    })
    .Build();

var store = new JsonFileWorkflowStore("./workflows");
var workflow = store.Get("retry-loop")
    ?? throw new InvalidOperationException("Workflow 'retry-loop' not found.");

var runner = host.Services.GetRequiredService<IWorkflowRunner>();

// Set the quality threshold — the loop retries until score meets this
var state = new WorkflowState();
state.Inputs["threshold"] = 0.8;

var result = await runner.RunAsync(workflow, state);

Console.WriteLine();
Console.WriteLine($"Errors: {result.Errors.Count}");
foreach (var e in result.Errors)
    Console.WriteLine($"  - {e}");

// ---------------------------------------------------------------------------
// AttemptStep — simulates an operation that produces a quality score.
// Each attempt gets a slightly better score (simulating iterative improvement).
// Reads the current attempt count from state to determine the score.
// ---------------------------------------------------------------------------
public class AttemptStep : IStep
{
    public string StepType => "attempt";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        // Read current attempt count from state (0 if first time)
        var attempt = 1;
        if (context.State.Context.TryGetValue("check", out var prev)
            && prev is IDictionary<string, object?> prevDict
            && prevDict.TryGetValue("attempt", out var a))
        {
            attempt = Convert.ToInt32(a) + 1;
        }

        // Simulate improving quality with each attempt
        var score = Math.Round(0.3 + (attempt * 0.2), 2);
        score = Math.Min(score, 1.0);

        Console.WriteLine($"  [attempt] Try #{attempt} → score: {score}");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?>
            {
                ["score"] = score,
                ["attempt"] = attempt
            }
        });
    }
}

// ---------------------------------------------------------------------------
// CheckStep — evaluates whether the score meets the threshold.
// Writes "needsRetry" to outputs, which the conditional loopback edge reads.
// ---------------------------------------------------------------------------
public class CheckStep : IStep
{
    public string StepType => "check";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var score = 0.0;
        if (context.State.Context.TryGetValue("attempt", out var attemptObj)
            && attemptObj is IDictionary<string, object?> attemptDict
            && attemptDict.TryGetValue("score", out var s))
        {
            score = Convert.ToDouble(s);
        }

        var threshold = 0.8;
        if (context.State.Inputs.TryGetValue("threshold", out var t))
            threshold = Convert.ToDouble(t);

        var attempt = 0;
        if (context.State.Context.TryGetValue("attempt", out var aObj)
            && aObj is IDictionary<string, object?> aDict
            && aDict.TryGetValue("attempt", out var aVal))
        {
            attempt = Convert.ToInt32(aVal);
        }

        var needsRetry = score < threshold;

        Console.WriteLine($"  [check] Score {score} vs threshold {threshold} → {(needsRetry ? "RETRY" : "PASS")}");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?>
            {
                ["needsRetry"] = needsRetry,
                ["score"] = score,
                ["attempt"] = attempt
            }
        });
    }
}

// ---------------------------------------------------------------------------
// DoneStep — final step, prints the result.
// ---------------------------------------------------------------------------
public class DoneStep : IStep
{
    public string StepType => "done";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var score = 0.0;
        var attempt = 0;

        if (context.State.Context.TryGetValue("check", out var checkObj)
            && checkObj is IDictionary<string, object?> checkDict)
        {
            if (checkDict.TryGetValue("score", out var s))
                score = Convert.ToDouble(s);
            if (checkDict.TryGetValue("attempt", out var a))
                attempt = Convert.ToInt32(a);
        }

        Console.WriteLine($"  [done] Accepted after {attempt} attempt(s) with score {score}");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?>
            {
                ["finalScore"] = score,
                ["totalAttempts"] = attempt
            }
        });
    }
}