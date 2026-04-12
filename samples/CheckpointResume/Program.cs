using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Execution;
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
            spectra.AddStep(new ValidateStep());
            spectra.AddStep(new ProcessStep());
            spectra.AddStep(new ConfirmStep());

            // File-based checkpoints — persists to disk so we can resume across runs
            spectra.AddFileCheckpoints(checkpointDir);
            spectra.AddConsoleEvents();
        });
    })
    .Build();

var store = new JsonFileWorkflowStore("./workflows");
var workflow = store.Get("order-pipeline")
    ?? throw new InvalidOperationException("Workflow 'order-pipeline' not found.");

var runner = host.Services.GetRequiredService<IWorkflowRunner>();
var checkpointStore = host.Services.GetRequiredService<ICheckpointStore>();

// ── Run 1: starts fresh, pauses at "process" ─────────────────────────
Console.WriteLine("═══ RUN 1: Starting order pipeline ═══");
Console.WriteLine();

var state = new WorkflowState();
state.Inputs["orderId"] = "ORD-2026-0042";
state.Inputs["amount"] = 1250.00;

var result1 = await runner.RunAsync(workflow, state);
var runId = result1.RunId;

Console.WriteLine();
Console.WriteLine($"Run 1 stopped. RunId: {runId}");
Console.WriteLine($"Errors: {result1.Errors.Count}");

// ── Inspect the checkpoint on disk ───────────────────────────────────
Console.WriteLine();
Console.WriteLine("═══ CHECKPOINT INSPECTION ═══");
Console.WriteLine();

var checkpoint = await checkpointStore.LoadAsync(runId);
if (checkpoint != null)
{
    Console.WriteLine($"  Status      : {checkpoint.Status}");
    Console.WriteLine($"  Last node   : {checkpoint.LastCompletedNodeId}");
    Console.WriteLine($"  Next node   : {checkpoint.NextNodeId}");
    Console.WriteLine($"  Steps done  : {checkpoint.StepsCompleted}");
    Console.WriteLine($"  Saved at    : {checkpoint.UpdatedAt:HH:mm:ss.fff}");
}

// ── Run 2: resume from checkpoint ────────────────────────────────────
Console.WriteLine();
Console.WriteLine("═══ RUN 2: Resuming from checkpoint ═══");
Console.WriteLine();

var result2 = await runner.ResumeAsync(workflow, runId);

Console.WriteLine();
Console.WriteLine($"Run 2 completed. Errors: {result2.Errors.Count}");
foreach (var e in result2.Errors)
    Console.WriteLine($"  - {e}");

// ── Cleanup ──────────────────────────────────────────────────────────
if (Directory.Exists(checkpointDir))
    Directory.Delete(checkpointDir, recursive: true);

// ---------------------------------------------------------------------------
// ValidateStep — validates the order. Always succeeds.
// ---------------------------------------------------------------------------
public class ValidateStep : IStep
{
    public string StepType => "validate";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var orderId = context.Inputs.TryGetValue("orderId", out var id)
            ? id?.ToString() ?? "unknown" : "unknown";
        var amount = 0.0;
        if (context.Inputs.TryGetValue("amount", out var a))
        {
            amount = a switch
            {
                double d => d,
                long l => l,
                System.Text.Json.JsonElement je => je.GetDouble(),
                _ => Convert.ToDouble(a)
            };
        }

        Console.WriteLine($"  [validate] Order {orderId} (${amount:N2}) — valid");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?>
            {
                ["orderId"] = orderId,
                ["amount"] = amount,
                ["valid"] = true
            }
        });
    }
}

// ---------------------------------------------------------------------------
// ProcessStep — simulates a payment processor.
// Returns NeedsContinuation on first call (simulating "pending"),
// then Succeeded on the second call (after resume).
// ---------------------------------------------------------------------------
public class ProcessStep : IStep
{
    public string StepType => "process";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        // Check if we already processed once (continuation marker in state)
        var alreadyAttempted = context.State.Context.ContainsKey("__processAttempted");

        if (!alreadyAttempted)
        {
            // First execution — mark as attempted and request continuation
            context.State.Context["__processAttempted"] = true;

            Console.WriteLine("  [process] Payment submitted — awaiting confirmation...");
            Console.WriteLine("  [process] → Returning NeedsContinuation (workflow will pause)");

            return Task.FromResult(new StepResult
            {
                Status = StepStatus.NeedsContinuation,
                Outputs = new Dictionary<string, object?>
                {
                    ["status"] = "pending"
                }
            });
        }

        // Second execution (after resume) — payment confirmed
        Console.WriteLine("  [process] Payment confirmed!");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?>
            {
                ["status"] = "confirmed",
                ["transactionId"] = "TXN-" + Guid.NewGuid().ToString()[..8]
            }
        });
    }
}

// ---------------------------------------------------------------------------
// ConfirmStep — sends order confirmation. Only runs after resume.
// ---------------------------------------------------------------------------
public class ConfirmStep : IStep
{
    public string StepType => "confirm";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var txnId = "unknown";
        if (context.State.Context.TryGetValue("process", out var pObj)
            && pObj is IDictionary<string, object?> pDict
            && pDict.TryGetValue("transactionId", out var t))
        {
            txnId = t?.ToString() ?? "unknown";
        }

        Console.WriteLine($"  [confirm] Order confirmed. Transaction: {txnId}");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?>
            {
                ["confirmed"] = true,
                ["transactionId"] = txnId
            }
        });
    }
}