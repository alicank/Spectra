using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.State;
using Spectra.Contracts.Streaming;
using Spectra.Contracts.Workflow;
using System.Text.Json;

namespace Spectra.AspNetCore;

/// <summary>
/// Extension methods to map Spectra workflow endpoints as ASP.NET Core middleware.
/// Follows the pattern of <c>app.MapHealthChecks()</c> and <c>app.MapOpenApi()</c>.
/// This is NOT a standalone host — it runs inside your app.
/// </summary>
public static class SpectraEndpointExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Maps Spectra workflow endpoints under the given prefix (default: <c>/spectra</c>).
    /// Endpoints: POST /run, GET /stream, GET /checkpoints/{runId}, POST /interrupt/{runId},
    /// POST /fork/{runId}.
    /// </summary>
    public static IEndpointRouteBuilder MapSpectra(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/spectra")
    {
        var group = endpoints.MapGroup(prefix)
            .WithTags("Spectra");

        // POST /spectra/run — execute a workflow
        group.MapPost("/run", async (HttpContext ctx, IWorkflowRunner runner) =>
        {
            var request = await ctx.Request.ReadFromJsonAsync<RunWorkflowRequest>(JsonOptions);
            if (request is null)
                return Results.BadRequest(new { error = "Invalid request body" });

            var workflow = ResolveWorkflow(ctx.RequestServices, request.WorkflowId, request.Workflow);
            if (workflow is null)
                return Results.BadRequest(new { error = $"Workflow '{request.WorkflowId}' not found" });

            var state = new WorkflowState();
            if (request.Inputs is not null)
            {
                foreach (var (key, value) in request.Inputs)
                    state.Inputs[key] = value;
            }

            var runContext = BuildRunContext(ctx);
            var result = await runner.RunAsync(workflow, state, runContext, ctx.RequestAborted);

            return Results.Ok(new RunWorkflowResponse
            {
                RunId = result.RunId,
                WorkflowId = workflow.Id,
                Success = result.Errors.Count == 0,
                Errors = result.Errors,
                Artifacts = result.Artifacts,
                Context = result.Context,
                CurrentNodeId = result.CurrentNodeId
            });
        });

        // GET /spectra/stream?workflowId=...&mode=... — stream workflow events via SSE
        group.MapGet("/stream", async (HttpContext ctx, IWorkflowRunner runner,
            string workflowId, string? mode) =>
        {
            var workflow = ResolveWorkflow(ctx.RequestServices, workflowId, null);
            if (workflow is null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { error = $"Workflow '{workflowId}' not found" });
                return;
            }

            var streamMode = Enum.TryParse<StreamMode>(mode, true, out var m) ? m : StreamMode.Updates;

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var runContext = BuildRunContext(ctx);
            await foreach (var evt in runner.StreamAsync(workflow, streamMode, null, runContext, ctx.RequestAborted))
            {
                var json = JsonSerializer.Serialize<object>(evt, JsonOptions);
                await ctx.Response.WriteAsync($"event: {evt.EventType}\ndata: {json}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
        });

        // GET /spectra/checkpoints/{runId} — list/inspect checkpoints
        group.MapGet("/checkpoints/{runId}", async (string runId, HttpContext ctx) =>
        {
            var store = ctx.RequestServices.GetService<ICheckpointStore>();
            if (store is null)
                return Results.BadRequest(new { error = "No checkpoint store configured" });

            var checkpoint = await store.LoadAsync(runId);
            if (checkpoint is null)
                return Results.NotFound(new { error = $"No checkpoint found for run '{runId}'" });

            return Results.Ok(new CheckpointResponse
            {
                RunId = checkpoint.RunId,
                WorkflowId = checkpoint.WorkflowId,
                Status = checkpoint.Status.ToString(),
                StepsCompleted = checkpoint.StepsCompleted,
                LastCompletedNodeId = checkpoint.LastCompletedNodeId,
                NextNodeId = checkpoint.NextNodeId,
                UpdatedAt = checkpoint.UpdatedAt
            });
        });

        // POST /spectra/interrupt/{runId} — submit an interrupt response
        group.MapPost("/interrupt/{runId}", async (HttpContext ctx, string runId,
            IWorkflowRunner runner) =>
        {
            var request = await ctx.Request.ReadFromJsonAsync<InterruptResponseRequest>(JsonOptions);
            if (request is null)
                return Results.BadRequest(new { error = "Invalid request body" });

            var workflow = ResolveWorkflow(ctx.RequestServices, request.WorkflowId, null);
            if (workflow is null)
                return Results.BadRequest(new { error = $"Workflow '{request.WorkflowId}' not found" });

            var interruptResponse = request.Approved
                ? InterruptResponse.ApprovedResponse(
                    payload: request.Data,
                    respondedBy: request.RespondedBy,
                    comment: request.Comment)
                : InterruptResponse.RejectedResponse(
                    respondedBy: request.RespondedBy,
                    comment: request.Comment,
                    payload: request.Data);

            // TODO: Wire RunContext into ResumeWithResponseAsync when the interface supports it
            var result = await runner.ResumeWithResponseAsync(
                workflow, runId, interruptResponse, ctx.RequestAborted);

            return Results.Ok(new RunWorkflowResponse
            {
                RunId = result.RunId,
                WorkflowId = workflow.Id,
                Success = result.Errors.Count == 0,
                Errors = result.Errors,
                Artifacts = result.Artifacts,
                Context = result.Context,
                CurrentNodeId = result.CurrentNodeId
            });
        });

        // POST /spectra/fork/{runId} — fork from a checkpoint and run
        group.MapPost("/fork/{runId}", async (HttpContext ctx, string runId,
            IWorkflowRunner runner) =>
        {
            var request = await ctx.Request.ReadFromJsonAsync<ForkRequest>(JsonOptions);
            if (request is null)
                return Results.BadRequest(new { error = "Invalid request body" });

            var workflow = ResolveWorkflow(ctx.RequestServices, request.WorkflowId, null);
            if (workflow is null)
                return Results.BadRequest(new { error = $"Workflow '{request.WorkflowId}' not found" });

            var result = await runner.ForkAndRunAsync(
                workflow, runId, request.CheckpointIndex,
                request.NewRunId, cancellationToken: ctx.RequestAborted);

            return Results.Ok(new RunWorkflowResponse
            {
                RunId = result.RunId,
                WorkflowId = workflow.Id,
                Success = result.Errors.Count == 0,
                Errors = result.Errors,
                Artifacts = result.Artifacts,
                Context = result.Context,
                CurrentNodeId = result.CurrentNodeId
            });
        });

        return endpoints;
    }

    private static WorkflowDefinition? ResolveWorkflow(
        IServiceProvider services, string? workflowId, WorkflowDefinition? inline)
    {
        if (inline is not null)
            return inline;

        if (workflowId is null)
            return null;

        var store = services.GetService<IWorkflowStore>();
        return store?.Get(workflowId);
    }

    /// <summary>
    /// Builds a <see cref="RunContext"/> from the current <see cref="HttpContext"/>.
    /// Maps the authenticated <see cref="ClaimsPrincipal"/> into Spectra's identity POCO.
    /// </summary>
    private static RunContext BuildRunContext(HttpContext ctx)
    {
        var user = ctx.User;
        if (user?.Identity?.IsAuthenticated != true)
            return RunContext.Anonymous;

        return new RunContext
        {
            UserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? user.FindFirst("sub")?.Value,
            TenantId = user.FindFirst("tenant_id")?.Value
                    ?? user.FindFirst("tid")?.Value,
            Roles = user.FindAll(ClaimTypes.Role)
                        .Select(c => c.Value)
                        .ToList(),
            Claims = user.Claims.ToList(),
            CorrelationId = ctx.TraceIdentifier,
            Metadata = new Dictionary<string, string>
            {
                ["remoteIp"] = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            }
        };
    }
}

// ── Request / Response DTOs ──

/// <summary>Request body for POST /spectra/run.</summary>
public class RunWorkflowRequest
{
    public string? WorkflowId { get; set; }
    public WorkflowDefinition? Workflow { get; set; }
    public Dictionary<string, object>? Inputs { get; set; }
}

/// <summary>Response body for workflow execution endpoints.</summary>
public class RunWorkflowResponse
{
    public string RunId { get; set; } = "";
    public string WorkflowId { get; set; } = "";
    public bool Success { get; set; }
    public List<string> Errors { get; set; } = [];
    public Dictionary<string, object?> Artifacts { get; set; } = [];
    public Dictionary<string, object?> Context { get; set; } = [];
    public string? CurrentNodeId { get; set; }
}

/// <summary>Response body for GET /spectra/checkpoints/{runId}.</summary>
public class CheckpointResponse
{
    public string RunId { get; set; } = "";
    public string WorkflowId { get; set; } = "";
    public string Status { get; set; } = "";
    public int StepsCompleted { get; set; }
    public string? LastCompletedNodeId { get; set; }
    public string? NextNodeId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Request body for POST /spectra/interrupt/{runId}.</summary>
public class InterruptResponseRequest
{
    public string WorkflowId { get; set; } = "";
    public bool Approved { get; set; }
    public string? RespondedBy { get; set; }
    public string? Comment { get; set; }
    public Dictionary<string, object?>? Data { get; set; }
}

/// <summary>Request body for POST /spectra/fork/{runId}.</summary>
public class ForkRequest
{
    public string WorkflowId { get; set; } = "";
    public int CheckpointIndex { get; set; }
    public string? NewRunId { get; set; }
}