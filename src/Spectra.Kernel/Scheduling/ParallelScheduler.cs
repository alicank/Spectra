using Spectra.Kernel.Validation;
using Spectra.Contracts.Diagnostics;
using Spectra.Kernel.Diagnostics;
using System.Diagnostics;
using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Evaluation;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Execution;

namespace Spectra.Kernel.Scheduling;

public class ParallelScheduler
{
    private readonly IStepRegistry _stepRegistry;
    private readonly IEventSink? _eventSink;
    private readonly ICheckpointStore? _checkpointStore;
    private readonly IConditionEvaluator _conditionEvaluator;
    private readonly int _maxConcurrency;
    private readonly IServiceProvider? _serviceProvider;
    private readonly IInterruptHandler? _interruptHandler;

    public ParallelScheduler(
        IStepRegistry stepRegistry,
        IEventSink? eventSink = null,
        ICheckpointStore? checkpointStore = null,
        IConditionEvaluator? conditionEvaluator = null,
        IServiceProvider? serviceProvider = null,
        IInterruptHandler? interruptHandler = null,
        int maxConcurrency = 4)
    {
        _stepRegistry = stepRegistry;
        _eventSink = eventSink;
        _checkpointStore = checkpointStore;
        _conditionEvaluator = conditionEvaluator ?? new DefaultConditionEvaluator();
        _serviceProvider = serviceProvider;
        _interruptHandler = interruptHandler;
        _maxConcurrency = maxConcurrency;
    }

    public async Task<WorkflowState> ExecuteAsync(
        WorkflowDefinition workflow,
        WorkflowState? initialState = null,
        CancellationToken cancellationToken = default)
    {
        var state = initialState ?? new WorkflowState { WorkflowId = workflow.Id };
        state.WorkflowId = workflow.Id;

        // ── Pre-run structural validation ──
        var validation = WorkflowValidator.Validate(workflow);
        if (!validation.IsValid)
        {
            state.Errors.AddRange(validation.Errors);

            await EmitAsync(new WorkflowCompletedEvent
            {
                RunId = state.RunId,
                WorkflowId = workflow.Id,
                EventType = nameof(WorkflowCompletedEvent),
                Success = false,
                Errors = validation.Errors
            });

            return state;
        }

        using var workflowActivity = SpectraActivitySource.StartWorkflow(
            workflow.Id, state.RunId, workflow.Name);

        var nodeMap = workflow.Nodes.ToDictionary(n => n.Id);
        var plan = ExecutionPlan.Build(workflow);
        var workflowStopwatch = Stopwatch.StartNew();
        var stepsExecuted = 0;

        // Semaphore for concurrency control
        using var semaphore = new SemaphoreSlim(_maxConcurrency);

        await EmitAsync(new WorkflowStartedEvent
        {
            RunId = state.RunId,
            WorkflowId = workflow.Id,
            EventType = nameof(WorkflowStartedEvent),
            WorkflowName = workflow.Name,
            TotalNodes = workflow.Nodes.Count
        });

        // Lock for thread-safe state access
        var stateLock = new object();

        while (!plan.IsComplete() && !plan.HasFailed())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var readyNodes = plan.GetReadyNodes().ToList();

            if (readyNodes.Count == 0)
            {
                // No ready nodes but not complete — might be waiting for parallel tasks
                await Task.Delay(10, cancellationToken);
                continue;
            }

            await EmitAsync(new ParallelBatchStartedEvent
            {
                RunId = state.RunId,
                WorkflowId = workflow.Id,
                EventType = nameof(ParallelBatchStartedEvent),
                NodeIds = readyNodes,
                BatchSize = readyNodes.Count
            });

            var batchStopwatch = Stopwatch.StartNew();
            using var batchActivity = SpectraActivitySource.StartBatch(
                workflow.Id, state.RunId, readyNodes.Count);

            // Execute ready nodes in parallel (up to max concurrency)
            var tasks = readyNodes.Select(async nodeId =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await ExecuteNodeAsync(
                        nodeId,
                        nodeMap[nodeId],
                        workflow,
                        state,
                        plan,
                        stateLock,
                        cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var results = await Task.WhenAll(tasks);

            batchStopwatch.Stop();

            var successCount = results.Count(r => r);
            var failureCount = results.Length - successCount;
            stepsExecuted += successCount;

            batchActivity?.SetTag(SpectraTags.BatchSuccessCount, successCount);
            batchActivity?.SetTag(SpectraTags.BatchFailureCount, failureCount);

            await EmitAsync(new ParallelBatchCompletedEvent
            {
                RunId = state.RunId,
                WorkflowId = workflow.Id,
                EventType = nameof(ParallelBatchCompletedEvent),
                NodeIds = readyNodes,
                SuccessCount = successCount,
                FailureCount = failureCount,
                Duration = batchStopwatch.Elapsed
            });

            // After batch completes, update ready status for dependent nodes
            UpdateReadyNodes(workflow, plan, state);
        }

        workflowStopwatch.Stop();

        workflowActivity?.SetTag(SpectraTags.StepsExecuted, stepsExecuted);
        if (plan.HasFailed() || state.Errors.Count > 0)
            workflowActivity?.SetStatus(ActivityStatusCode.Error,
                string.Join("; ", state.Errors));
        else
            workflowActivity?.SetStatus(ActivityStatusCode.Ok);

        await EmitAsync(new WorkflowCompletedEvent
        {
            RunId = state.RunId,
            WorkflowId = workflow.Id,
            EventType = nameof(WorkflowCompletedEvent),
            Success = !plan.HasFailed() && state.Errors.Count == 0,
            Duration = workflowStopwatch.Elapsed,
            StepsExecuted = stepsExecuted,
            Errors = state.Errors.ToList()
        });

        return state;
    }

    private async Task<bool> ExecuteNodeAsync(
        string nodeId,
        NodeDefinition node,
        WorkflowDefinition workflow,
        WorkflowState state,
        ExecutionPlan plan,
        object stateLock,
        CancellationToken cancellationToken)
    {
        // Mark as running
        plan.NodeStates[nodeId].Status = NodeExecutionStatus.Running;
        plan.NodeStates[nodeId].StartedAt = DateTimeOffset.UtcNow;

        var step = _stepRegistry.GetStep(node.StepType);
        if (step == null)
        {
            var error = $"Step type '{node.StepType}' not found";
            lock (stateLock) { state.Errors.Add(error); }
            plan.NodeStates[nodeId].Status = NodeExecutionStatus.Failed;

            await EmitAsync(new StepCompletedEvent
            {
                RunId = state.RunId,
                WorkflowId = workflow.Id,
                NodeId = nodeId,
                EventType = nameof(StepCompletedEvent),
                StepType = node.StepType,
                Status = StepStatus.Failed,
                ErrorMessage = error
            });
            return false;
        }

        Dictionary<string, object?> inputs;
        lock (stateLock)
        {
            var mapper = new StateMapper();
            inputs = mapper.ResolveInputs(node, state);
            inputs = mapper.ResolveInputs(node, state);
            if (!string.IsNullOrEmpty(node.AgentId) && !inputs.ContainsKey("agentId"))
            {
                inputs["agentId"] = node.AgentId;
            }
            if (!string.IsNullOrEmpty(node.SubgraphId) && !inputs.ContainsKey("__subgraphId"))
            {
                inputs["__subgraphId"] = node.SubgraphId;
            }
        }

        await EmitAsync(new StepStartedEvent
        {
            RunId = state.RunId,
            WorkflowId = workflow.Id,
            NodeId = nodeId,
            EventType = nameof(StepStartedEvent),
            StepType = node.StepType,
            Inputs = inputs
        });

        var context = new StepContext
        {
            RunId = state.RunId,
            WorkflowId = workflow.Id,
            NodeId = nodeId,
            State = state,
            CancellationToken = cancellationToken,
            Inputs = inputs,
            Services = _serviceProvider,
            WorkflowDefinition = workflow,
            Interrupt = _interruptHandler != null
                ? (request, ct) => _interruptHandler.HandleAsync(request, ct)
                : null
        };

        var stepStopwatch = Stopwatch.StartNew();
        using var stepActivity = SpectraActivitySource.StartStep(
            workflow.Id, state.RunId, nodeId, node.StepType);

        StepResult result;
        try
        {
            result = await step.ExecuteAsync(context);
        }
        catch (InterruptException ex)
        {
            stepStopwatch.Stop();
            plan.NodeStates[nodeId].CompletedAt = DateTimeOffset.UtcNow;
            plan.NodeStates[nodeId].Status = NodeExecutionStatus.Failed;

            stepActivity?.SetTag(SpectraTags.StepStatus, "interrupted");
            stepActivity?.SetTag(SpectraTags.InterruptReason,
                ex.Request.Reason ?? "Programmatic interrupt");

            await EmitAsync(new StepInterruptedEvent
            {
                RunId = state.RunId,
                WorkflowId = workflow.Id,
                NodeId = nodeId,
                EventType = nameof(StepInterruptedEvent),
                StepType = node.StepType,
                Reason = ex.Request.Reason ?? "Programmatic interrupt",
                InterruptTitle = ex.Request.Title,
                IsDeclarative = false
            });

            return false;
        }
        stepStopwatch.Stop();

        plan.NodeStates[nodeId].CompletedAt = DateTimeOffset.UtcNow;

        stepActivity?.SetTag(SpectraTags.StepStatus, result.Status.ToString());
        if (result.Status == StepStatus.Failed && result.ErrorMessage is not null)
            SpectraActivitySource.RecordError(stepActivity, result.ErrorMessage);

        await EmitAsync(new StepCompletedEvent
        {
            RunId = state.RunId,
            WorkflowId = workflow.Id,
            NodeId = nodeId,
            EventType = nameof(StepCompletedEvent),
            StepType = node.StepType,
            Status = result.Status,
            Duration = stepStopwatch.Elapsed,
            Outputs = result.Outputs,
            ErrorMessage = result.ErrorMessage
        });

        if (result.Status == StepStatus.Failed)
        {
            lock (stateLock) { state.Errors.Add(result.ErrorMessage ?? "Unknown error"); }
            plan.NodeStates[nodeId].Status = NodeExecutionStatus.Failed;
            return false;
        }

        if (result.Status == StepStatus.Interrupted)
        {
            plan.NodeStates[nodeId].Status = NodeExecutionStatus.Failed;

            await EmitAsync(new StepInterruptedEvent
            {
                RunId = state.RunId,
                WorkflowId = workflow.Id,
                NodeId = nodeId,
                EventType = nameof(StepInterruptedEvent),
                StepType = node.StepType,
                Reason = result.ErrorMessage ?? "Step interrupted",
                IsDeclarative = false
            });

            return false;
        }

        // Apply outputs thread-safely
        lock (stateLock)
        {
            var mapper = new StateMapper();
            mapper.ApplyOutputs(node, state, result.Outputs);
        }

        plan.NodeStates[nodeId].Status = NodeExecutionStatus.Completed;
        return true;
    }

    private void UpdateReadyNodes(WorkflowDefinition workflow, ExecutionPlan plan, WorkflowState state)
    {
        var nodeMap = workflow.Nodes.ToDictionary(n => n.Id);

        // Check pending nodes as before
        foreach (var nodeState in plan.NodeStates.Values.Where(n => n.Status == NodeExecutionStatus.Pending))
        {
            var nodeId = nodeState.NodeId;
            var node = nodeMap[nodeId];

            var incomingEdges = workflow.Edges.Where(e => e.To == nodeId).ToList();
            var satisfiedEdges = new List<EdgeDefinition>();

            foreach (var edge in incomingEdges)
            {
                if (plan.NodeStates[edge.From].Status != NodeExecutionStatus.Completed)
                    continue;

                if (string.IsNullOrEmpty(edge.Condition))
                {
                    satisfiedEdges.Add(edge);
                }
                else
                {
                    var conditionResult = _conditionEvaluator.Evaluate(edge.Condition, state);
                    if (conditionResult.Satisfied)
                    {
                        satisfiedEdges.Add(edge);
                    }
                }
            }

            bool isReady;
            if (node.WaitForAll)
            {
                isReady = satisfiedEdges.Count > 0 &&
                          satisfiedEdges.Count == incomingEdges.Count(e =>
                              plan.NodeStates[e.From].Status == NodeExecutionStatus.Completed);
            }
            else
            {
                isReady = satisfiedEdges.Count > 0;
            }

            if (isReady)
            {
                nodeState.Status = NodeExecutionStatus.Ready;
            }
        }

        // Check loopback edges targeting already-completed nodes
        foreach (var edge in workflow.Edges.Where(e => e.IsLoopback))
        {
            var targetState = plan.NodeStates[edge.To];
            if (targetState.Status != NodeExecutionStatus.Completed)
                continue;

            if (plan.NodeStates[edge.From].Status != NodeExecutionStatus.Completed)
                continue;

            // Evaluate loopback condition
            bool shouldLoop;
            if (string.IsNullOrEmpty(edge.Condition))
            {
                shouldLoop = true;
            }
            else
            {
                var conditionResult = _conditionEvaluator.Evaluate(edge.Condition, state);
                shouldLoop = conditionResult.Satisfied;
            }

            if (shouldLoop && targetState.IterationCount < workflow.MaxNodeIterations)
            {
                targetState.Status = NodeExecutionStatus.Ready;
                targetState.IterationCount++;
            }
            else if (shouldLoop && targetState.IterationCount >= workflow.MaxNodeIterations)
            {
                // Safety cap reached — treat as failure
                targetState.Status = NodeExecutionStatus.Failed;
                state.Errors.Add($"Node '{edge.To}' exceeded maximum iterations ({workflow.MaxNodeIterations})");
            }
        }
    }


    private async Task EmitAsync(WorkflowEvent evt)
    {
        if (_eventSink != null)
        {
            await _eventSink.PublishAsync(evt);
        }
    }

    private class DefaultConditionEvaluator : IConditionEvaluator
    {
        public ConditionResult Evaluate(string expression, WorkflowState state) =>
            string.IsNullOrWhiteSpace(expression)
                ? ConditionResult.True()
                : ConditionResult.False();
    }
}