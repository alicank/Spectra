using System.Diagnostics;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Prompts;
using Spectra.Contracts.Providers;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Tools;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Prompts;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Built-in tool injected into supervisor agents. When called, it actually
/// executes the target worker agent inline (recursive <see cref="AgentStep"/>
/// execution) and returns the worker's response as the tool result.
/// This allows the supervisor to maintain its loop context across delegations.
/// </summary>
internal class DelegateToAgentTool : ITool
{
    private readonly List<string> _allowedWorkers;
    private readonly IProviderRegistry _providerRegistry;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IToolRegistry _toolRegistry;
    private readonly PromptRenderer _promptRenderer;
    private readonly IPromptRegistry? _promptRegistry;
    private readonly IEventSink? _eventSink;

    public DelegateToAgentTool(
        IEnumerable<string> allowedWorkers,
        IProviderRegistry providerRegistry,
        IAgentRegistry agentRegistry,
        IToolRegistry toolRegistry,
        PromptRenderer promptRenderer,
        IPromptRegistry? promptRegistry = null,
        IEventSink? eventSink = null)
    {
        _allowedWorkers = allowedWorkers.ToList();
        _providerRegistry = providerRegistry;
        _agentRegistry = agentRegistry;
        _toolRegistry = toolRegistry;
        _promptRenderer = promptRenderer;
        _promptRegistry = promptRegistry;
        _eventSink = eventSink;
    }

    public string Name => "delegate_to_agent";

    public ToolDefinition Definition => new()
    {
        Name = "delegate_to_agent",
        Description = BuildDescription(),
        Parameters =
        [
            new ToolParameter
            {
                Name = "worker_agent",
                Description = $"The worker agent to delegate to. Must be one of: {string.Join(", ", _allowedWorkers)}",
                Type = "string",
                Required = true
            },
            new ToolParameter
            {
                Name = "task",
                Description = "The task description for the worker agent",
                Type = "string",
                Required = true
            },
            new ToolParameter
            {
                Name = "constraints",
                Description = "Comma-separated constraints the worker must respect",
                Type = "string",
                Required = false
            }
        ]
    };

    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> arguments,
        WorkflowState state,
        CancellationToken ct = default)
    {
        var workerAgentId = arguments.TryGetValue("worker_agent", out var w) ? w?.ToString() : null;
        var task = arguments.TryGetValue("task", out var t) ? t?.ToString() : null;
        var constraintsRaw = arguments.TryGetValue("constraints", out var c) ? c?.ToString() : null;

        if (string.IsNullOrEmpty(workerAgentId))
            return ToolResult.Fail("worker_agent is required.");

        if (!_allowedWorkers.Contains(workerAgentId, StringComparer.OrdinalIgnoreCase))
            return ToolResult.Fail($"Worker '{workerAgentId}' is not in the allowed worker list. " +
                                   $"Available: {string.Join(", ", _allowedWorkers)}");

        if (string.IsNullOrEmpty(task))
            return ToolResult.Fail("task is required.");

        // Check delegation depth from execution context
        var execCtx = AgentExecutionContextHelper.GetFromState(state);
        if (execCtx is not null && execCtx.DelegationDepth >= GetMaxDelegationDepth(workerAgentId))
        {
            return ToolResult.Fail(
                $"Maximum delegation depth ({execCtx.DelegationDepth}) reached. " +
                "Cannot delegate further. Complete the task with available information.");
        }

        // Check global budget
        if (execCtx is not null && execCtx.GlobalBudgetRemaining > 0 && execCtx.TotalTokensConsumed >= execCtx.GlobalBudgetRemaining)
        {
            return ToolResult.Fail("Global token budget exhausted. Cannot delegate further.");
        }

        // Check wall-clock deadline
        if (execCtx?.WallClockDeadline is not null && DateTimeOffset.UtcNow >= execCtx.WallClockDeadline)
        {
            return ToolResult.Fail("Wall-clock deadline exceeded. Cannot delegate further.");
        }

        var workerAgent = _agentRegistry.GetAgent(workerAgentId);
        if (workerAgent is null)
            return ToolResult.Fail($"Worker agent '{workerAgentId}' not found in agent registry.");

        // Resolve worker's tools from its definition or node parameters
        // Workers use their own tool set, not the supervisor's
        var workerToolNames = ResolveWorkerTools(workerAgentId);

        // Build child execution context
        var childExecCtx = execCtx?.Fork() ?? new AgentExecutionContext();
        childExecCtx.DelegationDepth++;
        childExecCtx.ParentAgentId = GetSupervisorAgentId(state);

        // Build worker inputs
        var workerInputs = new Dictionary<string, object?>
        {
            ["agentId"] = workerAgentId,
            ["userPrompt"] = task,
            [AgentExecutionContextHelper.ContextKey] = childExecCtx
        };

        if (workerToolNames.Count > 0)
            workerInputs["tools"] = workerToolNames.ToArray();

        // Apply constraints as part of the system prompt augmentation
        if (!string.IsNullOrEmpty(constraintsRaw))
        {
            var constraints = constraintsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            workerInputs["__delegationConstraints"] = constraints;
        }

        // Emit delegation started event
        if (_eventSink is not null)
        {
            await _eventSink.PublishAsync(new AgentDelegationStartedEvent
            {
                RunId = state.RunId,
                WorkflowId = state.WorkflowId,
                EventType = nameof(AgentDelegationStartedEvent),
                SupervisorAgent = GetSupervisorAgentId(state) ?? "unknown",
                WorkerAgent = workerAgentId,
                Task = task,
                DelegationDepth = childExecCtx.DelegationDepth,
                BudgetAllocated = childExecCtx.GlobalBudgetRemaining
            }, ct);
        }

        // Execute worker agent inline
        var sw = Stopwatch.StartNew();
        var workerStep = new AgentStep(
            _providerRegistry, _agentRegistry, _toolRegistry,
            _promptRenderer, _promptRegistry, _eventSink);

        var workerContext = new StepContext
        {
            RunId = state.RunId,
            WorkflowId = state.WorkflowId,
            NodeId = $"delegation:{workerAgentId}:{childExecCtx.DelegationDepth}",
            State = state,
            CancellationToken = ct,
            Inputs = workerInputs
        };

        StepResult workerResult;
        try
        {
            workerResult = await workerStep.ExecuteAsync(workerContext);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ToolResult.Fail($"Worker agent '{workerAgentId}' failed with exception: {ex.Message}");
        }
        sw.Stop();

        // Update parent execution context with consumed tokens
        if (execCtx is not null && workerResult.Outputs.TryGetValue("totalInputTokens", out var inTok)
            && workerResult.Outputs.TryGetValue("totalOutputTokens", out var outTok))
        {
            var consumed = (inTok is int i ? i : 0) + (outTok is int o ? o : 0);
            execCtx.TotalTokensConsumed += consumed;
            if (execCtx.GlobalBudgetRemaining > 0)
                execCtx.GlobalBudgetRemaining = Math.Max(0, execCtx.GlobalBudgetRemaining - consumed);
        }

        // Emit delegation completed event
        if (_eventSink is not null)
        {
            var response = workerResult.Outputs.TryGetValue("response", out var r) ? r?.ToString() : null;
            await _eventSink.PublishAsync(new AgentDelegationCompletedEvent
            {
                RunId = state.RunId,
                WorkflowId = state.WorkflowId,
                EventType = nameof(AgentDelegationCompletedEvent),
                SupervisorAgent = GetSupervisorAgentId(state) ?? "unknown",
                WorkerAgent = workerAgentId,
                Status = workerResult.Status.ToString(),
                TokensUsed = (workerResult.Outputs.TryGetValue("totalInputTokens", out var it) ? it is int ii ? ii : 0 : 0)
                           + (workerResult.Outputs.TryGetValue("totalOutputTokens", out var ot) ? ot is int oo ? oo : 0 : 0),
                Duration = sw.Elapsed,
                ResultSummary = response?.Length > 500 ? response[..500] + "..." : response
            }, ct);
        }

        if (workerResult.Status == StepStatus.Failed)
        {
            return ToolResult.Fail(
                $"Worker '{workerAgentId}' failed: {workerResult.ErrorMessage ?? "Unknown error"}");
        }

        var workerResponse = workerResult.Outputs.TryGetValue("response", out var resp)
            ? resp?.ToString() ?? "(no response)"
            : "(no response)";

        return ToolResult.Ok(workerResponse);
    }

    private List<string> ResolveWorkerTools(string workerAgentId)
    {
        // Workers use all tools registered to them. For now, return all available tools
        // except the delegation tools themselves to prevent infinite recursion.
        // In a fuller implementation, workers would have their own tool whitelist
        // configured via the workflow definition.
        return _toolRegistry.GetAll()
            .Where(t => t.Name != "delegate_to_agent" && t.Name != "transfer_to_agent")
            .Select(t => t.Name)
            .ToList();
    }

    private int GetMaxDelegationDepth(string workerAgentId)
    {
        var worker = _agentRegistry.GetAgent(workerAgentId);
        return worker?.MaxDelegationDepth ?? 3;
    }

    private static string? GetSupervisorAgentId(WorkflowState state)
    {
        var ctx = AgentExecutionContextHelper.GetFromState(state);
        return ctx?.ParentAgentId;
    }

    private string BuildDescription()
    {
        var workers = string.Join(", ", _allowedWorkers);
        return $"Delegate a task to a worker agent and wait for the result. " +
               $"The worker executes autonomously and returns its response. " +
               $"Available workers: {workers}. " +
               $"Use this to break down complex tasks and assign sub-tasks to specialists.";
    }
}