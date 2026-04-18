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
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <summary>
    /// Maps Spectra workflow endpoints under the given prefix (default: <c>/spectra</c>).
    /// Endpoints: POST /run, GET /stream, GET /checkpoints/{runId}, POST /interrupt/{runId},
    /// POST /fork/{runId}.
    /// </summary>
    /// <returns>
    /// A convention builder so callers can chain <c>.RequireAuthorization()</c>,
    /// <c>.RequireCors()</c>, <c>.WithTags()</c>, etc.
    /// </returns>
    public static RouteGroupBuilder MapSpectra(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/spectra")
    {
        var group = endpoints.MapGroup(prefix)
            .WithTags("Spectra");

        // POST /spectra/run — execute a workflow
        group.MapPost("/run", async (HttpContext ctx, IWorkflowRunner runner) =>
        {
            RunWorkflowRequest? request;
            try
            {
                request = await ctx.Request.ReadFromJsonAsync<RunWorkflowRequest>(JsonOptions);
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
            }

            if (request is null)
                return Results.BadRequest(new { error = "Request body is required" });

            var workflow = ResolveWorkflow(ctx.RequestServices, request.WorkflowId, request.Workflow);
            if (workflow is null)
            {
                var which = string.IsNullOrEmpty(request.WorkflowId)
                    ? "(no workflowId or inline workflow provided)"
                    : $"'{request.WorkflowId}'";
                return Results.BadRequest(new { error = $"Workflow {which} not found" });
            }

            var state = new WorkflowState();
            if (request.Inputs is not null)
            {
                foreach (var (key, value) in request.Inputs)
                    state.Inputs[key] = value;
            }

            var runContext = BuildRunContext(ctx);
            var result = await runner.RunAsync(workflow, state, runContext, ctx.RequestAborted);

            return Results.Ok(ToRunResponse(result, workflow.Id));
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

            // SSE response headers — set and flush before first event so browsers open the stream immediately
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";
            ctx.Response.Headers["X-Accel-Buffering"] = "no"; // disable nginx buffering

            await ctx.Response.WriteAsync(": connected\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

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

            var checkpoint = await store.LoadAsync(runId, ctx.RequestAborted);
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
                UpdatedAt = checkpoint.UpdatedAt,
                PendingInterrupt = checkpoint.PendingInterrupt is null ? null : new PendingInterruptInfo
                {
                    NodeId = checkpoint.PendingInterrupt.NodeId,
                    Reason = checkpoint.PendingInterrupt.Reason,
                    Title = checkpoint.PendingInterrupt.Title,
                    Description = checkpoint.PendingInterrupt.Description
                }
            });
        });

        // POST /spectra/interrupt/{runId} — submit an interrupt response
        group.MapPost("/interrupt/{runId}", async (HttpContext ctx, string runId,
            IWorkflowRunner runner) =>
        {
            InterruptResponseRequest? request;
            try
            {
                request = await ctx.Request.ReadFromJsonAsync<InterruptResponseRequest>(JsonOptions);
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
            }

            if (request is null)
                return Results.BadRequest(new { error = "Request body is required" });

            var workflow = ResolveWorkflow(ctx.RequestServices, request.WorkflowId, null);
            if (workflow is null)
                return Results.BadRequest(new { error = $"Workflow '{request.WorkflowId}' not found" });

            // Resolve the interrupt status: prefer explicit `status` field, fall back to `approved` bool.
            var status = ResolveInterruptStatus(request);
            var interruptResponse = new InterruptResponse
            {
                Status = status,
                RespondedBy = request.RespondedBy ?? ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                Comment = request.Comment,
                Payload = request.Data
            };

            var result = await runner.ResumeWithResponseAsync(
                workflow, runId, interruptResponse, ctx.RequestAborted);

            return Results.Ok(ToRunResponse(result, workflow.Id));
        });

        // POST /spectra/fork/{runId} — fork from a checkpoint and run
        group.MapPost("/fork/{runId}", async (HttpContext ctx, string runId,
            IWorkflowRunner runner) =>
        {
            ForkRequest? request;
            try
            {
                request = await ctx.Request.ReadFromJsonAsync<ForkRequest>(JsonOptions);
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
            }

            if (request is null)
                return Results.BadRequest(new { error = "Request body is required" });

            var workflow = ResolveWorkflow(ctx.RequestServices, request.WorkflowId, null);
            if (workflow is null)
                return Results.BadRequest(new { error = $"Workflow '{request.WorkflowId}' not found" });

            var result = await runner.ForkAndRunAsync(
                workflow, runId, request.CheckpointIndex,
                request.NewRunId, cancellationToken: ctx.RequestAborted);

            return Results.Ok(ToRunResponse(result, workflow.Id));
        });

        return group;
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static WorkflowDefinition? ResolveWorkflow(
        IServiceProvider services, string? workflowId, WorkflowDefinition? inline)
    {
        if (inline is not null)
            return inline;

        if (string.IsNullOrEmpty(workflowId))
            return null;

        var store = services.GetService<IWorkflowStore>();
        return store?.Get(workflowId);
    }

    private static RunWorkflowResponse ToRunResponse(WorkflowState result, string workflowId) => new()
    {
        RunId = result.RunId,
        WorkflowId = workflowId,
        Status = result.Status.ToString(),
        Success = result.Status == WorkflowRunStatus.Completed,
        Errors = result.Errors,
        Artifacts = result.Artifacts,
        Context = result.Context,
        CurrentNodeId = result.CurrentNodeId
    };

    /// <summary>
    /// Resolves the interrupt status from the request. Prefers the explicit <c>status</c> field
    /// when present (case-insensitive match against <see cref="InterruptStatus"/>), otherwise
    /// falls back to the <c>approved</c> boolean for backward-compatible simple clients.
    /// </summary>
    private static InterruptStatus ResolveInterruptStatus(InterruptResponseRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<InterruptStatus>(request.Status, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return request.Approved ? InterruptStatus.Approved : InterruptStatus.Rejected;
    }

    /// <summary>
    /// Builds a <see cref="RunContext"/> from the current <see cref="HttpContext"/>.
    /// </summary>
    private static RunContext BuildRunContext(HttpContext ctx)
    {
        var user = ctx.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return new RunContext
            {
                CorrelationId = ctx.TraceIdentifier,
                Metadata = new Dictionary<string, string>
                {
                    ["remoteIp"] = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"
                }
            };
        }

        return new RunContext
        {
            UserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? user.FindFirst("sub")?.Value,
            TenantId = user.FindFirst("tenant_id")?.Value
                    ?? user.FindFirst("tid")?.Value,
            Roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(),
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

    /// <summary>
    /// Terminal status of the run: "Completed", "Failed", "Cancelled", "Interrupted", "AwaitingInput".
    /// </summary>
    public string Status { get; set; } = "";

    /// <summary>True only when <see cref="Status"/> is "Completed".</summary>
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

    /// <summary>
    /// If the run is paused waiting for an interrupt response, this surfaces the
    /// details the human approver needs to make a decision. Null for normal runs.
    /// </summary>
    public PendingInterruptInfo? PendingInterrupt { get; set; }
}

/// <summary>Describes a pending interrupt surfaced on the checkpoint response.</summary>
public class PendingInterruptInfo
{
    public string NodeId { get; set; } = "";
    public string? Reason { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Request body for POST /spectra/interrupt/{runId}.
/// Clients may send either:
///   - <c>{ "approved": true/false }</c> for simple approve/reject flows, or
///   - <c>{ "status": "Approved" | "Rejected" | "TimedOut" | "Cancelled" }</c> for full control.
/// If both are present, <c>status</c> wins.
/// </summary>
public class InterruptResponseRequest
{
    public string WorkflowId { get; set; } = "";

    /// <summary>
    /// Simple form: true = approve, false = reject. Used when <see cref="Status"/> is not set.
    /// </summary>
    public bool Approved { get; set; }

    /// <summary>
    /// Full form: "Approved", "Rejected", "TimedOut", "Cancelled" (case-insensitive).
    /// Takes precedence over <see cref="Approved"/> when present.
    /// </summary>
    public string? Status { get; set; }

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