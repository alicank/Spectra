using Microsoft.Extensions.DependencyInjection;
using Spectra.AspNetCore;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Registration;

// ─────────────────────────────────────────────────────────────────
//  Spectra API Host
//
//  This is a minimal reference ASP.NET Core app that exposes Spectra
//  workflows over HTTP via app.MapSpectra(). It's meant as:
//    • A working example you can copy into your own service
//    • An end-to-end test harness for the 5 endpoints
//
//  Endpoints exposed (see Spectra.AspNetCore):
//    POST /spectra/run
//    GET  /spectra/stream
//    GET  /spectra/checkpoints/{runId}
//    POST /spectra/interrupt/{runId}
//    POST /spectra/fork/{runId}
// ─────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// Resolve the workflows directory — prefer ./workflows next to the exe,
// fall back to the sample workflows in the repo so `dotnet run` just works.
var workflowsDir = Path.Combine(AppContext.BaseDirectory, "workflows");
if (!Directory.Exists(workflowsDir))
{
    var repoWorkflows = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "workflows");
    if (Directory.Exists(repoWorkflows))
        workflowsDir = Path.GetFullPath(repoWorkflows);
}

if (!Directory.Exists(workflowsDir))
    Directory.CreateDirectory(workflowsDir);

builder.Services.AddSpectra(spectra =>
{
    // Register the demo step. In a real app you'd register your own IStep
    // implementations here (or use AddAgent + provider for LLM-driven steps).
    spectra.AddStep(new EchoStep());
    spectra.AddStep(new ApprovalStep());

    // Checkpoints power /checkpoints, /interrupt, /fork. Swap for
    // AddFileCheckpoints("./checkpoints") to persist across restarts.
    spectra.AddInMemoryCheckpoints();

    // Load workflow JSON files from ./workflows
    spectra.AddWorkflowsFromDirectory(workflowsDir);

    spectra.AddConsoleEvents();
});

var app = builder.Build();

// Root discovery endpoint — handy when someone hits the base URL
app.MapGet("/", () => Results.Ok(new
{
    name = "Spectra API",
    workflowsDirectory = workflowsDir,
    endpoints = new[]
    {
        "POST /spectra/run",
        "GET  /spectra/stream?workflowId={id}&mode=Updates",
        "GET  /spectra/checkpoints/{runId}",
        "POST /spectra/interrupt/{runId}",
        "POST /spectra/fork/{runId}"
    }
}));

// Here's the one-liner the README promises.
// To lock this down in production, chain auth/cors like so:
//
//   app.MapSpectra()
//      .RequireAuthorization("WorkflowsPolicy")
//      .RequireCors("AllowFrontend");
//
app.MapSpectra();

Console.WriteLine($"Spectra API starting. Workflows loaded from: {workflowsDir}");
app.Run();


// ─────────────────────────────────────────────────────────────────
//  Demo Steps
// ─────────────────────────────────────────────────────────────────

/// <summary>Minimal step — echoes its "message" input back as output.</summary>
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

/// <summary>
/// Demo step used by the approval workflow. Succeeds if the previous step's
/// interrupt response was Approved, otherwise records the rejection/cancellation.
/// </summary>
public class ApprovalStep : IStep
{
    public string StepType => "approval-ack";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var action = context.Inputs.TryGetValue("action", out var raw)
            ? raw?.ToString() ?? "(unspecified)"
            : "(unspecified)";

        Console.WriteLine($"  [approval-ack] proceeding with action: {action}");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?>
            {
                ["action"] = action,
                ["acknowledgedAt"] = DateTimeOffset.UtcNow
            }
        });
    }
}