using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Registration;

const string checkpointDir = "./checkpoints";

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra =>
        {
            spectra.AddStep(new DraftStep());
            spectra.AddStep(new PublishStep());
            spectra.AddFileCheckpoints(checkpointDir, opts =>
            {
                opts.CheckpointOnInterrupt = true;
            });
            spectra.AddConsoleEvents();
        });
    })
    .Build();

var store = new JsonFileWorkflowStore("./workflows");
var workflow = store.Get("content-pipeline")
    ?? throw new InvalidOperationException("Workflow 'content-pipeline' not found.");

var runner = host.Services.GetRequiredService<IWorkflowRunner>();
var checkpointStore = host.Services.GetRequiredService<ICheckpointStore>();

// ── Run 1: draft succeeds, then pauses before publish ────────────────
Console.WriteLine("═══ RUN 1: Content pipeline — will pause for review ═══");
Console.WriteLine();

var state = new WorkflowState();
state.Inputs["topic"] = "Spectra v1.0 Release Notes";
state.Inputs["author"] = "Alican";

var result1 = await runner.RunAsync(workflow, state);
var runId = result1.RunId;

Console.WriteLine();
Console.WriteLine($"Run 1 paused. RunId: {runId}");
Console.WriteLine($"Errors: {result1.Errors.Count}");

// ── Inspect the interrupted checkpoint ───────────────────────────────
Console.WriteLine();
Console.WriteLine("═══ CHECKPOINT: Waiting for approval ═══");
Console.WriteLine();

var checkpoint = await checkpointStore.LoadAsync(runId);
if (checkpoint != null)
{
    Console.WriteLine($"  Status           : {checkpoint.Status}");
    Console.WriteLine($"  Next node        : {checkpoint.NextNodeId}");
    Console.WriteLine($"  Steps done       : {checkpoint.StepsCompleted}");
    Console.WriteLine($"  Pending interrupt: {(checkpoint.PendingInterrupt != null ? "yes" : "no")}");
    if (checkpoint.PendingInterrupt != null)
    {
        Console.WriteLine($"  Reason           : {checkpoint.PendingInterrupt.Reason}");
        Console.WriteLine($"  Title            : {checkpoint.PendingInterrupt.Title}");
    }
}

// ── Run 2: approve and resume ────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("═══ RUN 2: Approving and resuming ═══");
Console.WriteLine();

var approval = InterruptResponse.ApprovedResponse(
    respondedBy: "editor",
    comment: "Looks good — ship it!");

var result2 = await runner.ResumeWithResponseAsync(workflow, runId, approval);

Console.WriteLine();
Console.WriteLine($"Run 2 completed. Errors: {result2.Errors.Count}");
foreach (var e in result2.Errors)
    Console.WriteLine($"  - {e}");

// ── Cleanup ──────────────────────────────────────────────────────────
if (Directory.Exists(checkpointDir))
    Directory.Delete(checkpointDir, recursive: true);

// ---------------------------------------------------------------------------
// DraftStep — generates a draft document from the topic.
// ---------------------------------------------------------------------------
public class DraftStep : IStep
{
    public string StepType => "draft";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var topic = context.Inputs.TryGetValue("topic", out var t)
            ? t?.ToString() ?? "untitled" : "untitled";
        var author = context.Inputs.TryGetValue("author", out var a)
            ? a?.ToString() ?? "unknown" : "unknown";

        var draft = $"# {topic}\n\nBy {author}\n\nSpectra v1.0 brings graph-based AI workflow orchestration to .NET...";

        Console.WriteLine($"  [draft] Generated draft for \"{topic}\" by {author}");
        Console.WriteLine($"  [draft] Content: {draft.Length} chars");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?>
            {
                ["content"] = draft,
                ["topic"] = topic,
                ["author"] = author
            }
        });
    }
}

// ---------------------------------------------------------------------------
// PublishStep — publishes the approved content.
// This node has InterruptBefore in the JSON, so the runner pauses
// BEFORE this step executes. It only runs after approval.
// ---------------------------------------------------------------------------
public class PublishStep : IStep
{
    public string StepType => "publish";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var topic = "unknown";
        if (context.State.Context.TryGetValue("draft", out var dObj)
            && dObj is IDictionary<string, object?> dDict
            && dDict.TryGetValue("topic", out var t))
        {
            topic = t?.ToString() ?? "unknown";
        }

        Console.WriteLine($"  [publish] Publishing \"{topic}\" — approved and live!");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?>
            {
                ["published"] = true,
                ["publishedAt"] = DateTimeOffset.UtcNow.ToString("o")
            }
        });
    }
}