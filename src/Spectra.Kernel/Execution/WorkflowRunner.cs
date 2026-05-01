using Spectra.Kernel.Validation;
using Spectra.Contracts.Audit;
using Spectra.Contracts.Memory;
using Spectra.Contracts.Diagnostics;
using Spectra.Kernel.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Spectra.Contracts.Streaming;
using System.Diagnostics;
using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Evaluation;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.Providers;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;

namespace Spectra.Kernel.Execution;

public class WorkflowRunner : IWorkflowRunner
{
    private readonly IStepRegistry _registry;
    private readonly IStateMapper _stateMapper;
    private readonly IConditionEvaluator _conditionEvaluator;
    private readonly IEventSink? _eventSink;
    private readonly ICheckpointStore? _checkpointStore;
    private readonly CheckpointOptions _checkpointOptions;
    private readonly IAgentRegistry? _agentRegistry;
    private readonly IProviderRegistry? _providerRegistry;
    private readonly IInterruptHandler? _interruptHandler;
    private readonly IServiceProvider? _services;
    private readonly IMemoryStore? _memoryStore;
    private RunContext _runContext = RunContext.Anonymous;

    public WorkflowRunner(
        IStepRegistry registry,
        IStateMapper stateMapper,
        IConditionEvaluator? conditionEvaluator = null,
        IEventSink? eventSink = null,
        ICheckpointStore? checkpointStore = null,
        CheckpointOptions? checkpointOptions = null,
        IAgentRegistry? agentRegistry = null,
        IProviderRegistry? providerRegistry = null,
        IInterruptHandler? interruptHandler = null,
        IServiceProvider? services = null,
        IMemoryStore? memoryStore = null)
    {
        _registry = registry;
        _stateMapper = stateMapper;
        _conditionEvaluator = conditionEvaluator ?? new DefaultConditionEvaluator();
        _eventSink = eventSink;
        _checkpointStore = checkpointStore;
        _checkpointOptions = checkpointOptions ?? CheckpointOptions.Default;
        _agentRegistry = agentRegistry;
        _providerRegistry = providerRegistry;
        _interruptHandler = interruptHandler;
        _services = services;
        _memoryStore = memoryStore;
    }

    public IAsyncEnumerable<WorkflowEvent> StreamAsync(
        WorkflowDefinition workflow,
        StreamMode mode,
        WorkflowState? initialState,
        RunContext runContext,
        CancellationToken cancellationToken = default)
    {
        _runContext = runContext ?? RunContext.Anonymous;
        return StreamAsync(workflow, mode, initialState, cancellationToken);
    }

    public async IAsyncEnumerable<WorkflowEvent> StreamAsync(
        WorkflowDefinition workflow,
        StreamMode mode = StreamMode.Tokens,
        WorkflowState? initialState = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<WorkflowEvent>(
            new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });

        var streamingSink = new StreamingEventSink(channel.Writer);

        IEventSink compositeSink = _eventSink != null
            ? new CompositeEventSink(new[] { _eventSink, streamingSink })
            : streamingSink;

        var streamingRunner = new WorkflowRunner(
            _registry, _stateMapper, _conditionEvaluator, compositeSink,
            _checkpointStore, _checkpointOptions, _agentRegistry, _providerRegistry,
            _interruptHandler, _services, _memoryStore);

        streamingRunner._streamingChannel = channel;

        var runTask = Task.Run(async () =>
        {
            try { await streamingRunner.RunAsync(workflow, initialState, cancellationToken); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await streamingSink.PublishAsync(new WorkflowCompletedEvent
                {
                    RunId = initialState?.RunId ?? "unknown",
                    WorkflowId = workflow.Id,
                    EventType = nameof(WorkflowCompletedEvent),
                    Success = false,
                    Errors = [ex.Message]
                }, CancellationToken.None);
            }
            finally { streamingSink.Complete(); }
        }, cancellationToken);

        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
        {
            if (ShouldYield(evt, mode))
                yield return evt;
        }

        await runTask;
    }

    internal Channel<WorkflowEvent>? _streamingChannel;

    private static bool ShouldYield(WorkflowEvent evt, StreamMode mode) => mode switch
    {
        StreamMode.Tokens => true,
        StreamMode.Messages => evt is not TokenStreamEvent,
        StreamMode.Updates => evt is StepCompletedEvent or StateChangedEvent
                                 or WorkflowCompletedEvent or StepInterruptedEvent,
        StreamMode.Values => evt is StateChangedEvent or WorkflowCompletedEvent,
        StreamMode.Custom => true,
        _ => true
    };

    public Task<WorkflowState> RunAsync(
        WorkflowDefinition workflow,
        WorkflowState? initialState,
        RunContext runContext,
        CancellationToken cancellationToken = default)
    {
        _runContext = runContext ?? RunContext.Anonymous;
        return RunAsync(workflow, initialState, cancellationToken);
    }

    public async Task<WorkflowState> RunAsync(
        WorkflowDefinition workflow,
        WorkflowState? initialState = null,
        CancellationToken cancellationToken = default)
    {
        var state = initialState ?? new WorkflowState();
        state.WorkflowId = workflow.Id;
        state.Status = WorkflowRunStatus.InProgress;

        // ── Pre-run structural validation ──
        var validation = WorkflowValidator.Validate(workflow);
        if (!validation.IsValid)
        {
            state.Errors.AddRange(validation.Errors);
            state.Status = WorkflowRunStatus.Failed;

            await EmitAsync(new WorkflowCompletedEvent
            {
                RunId = state.RunId,
                WorkflowId = workflow.Id,
                EventType = nameof(WorkflowCompletedEvent),
                Success = false,
                Errors = validation.Errors
            }, CancellationToken.None);

            return state;
        }

        foreach (var warning in validation.Warnings)
        {
            await EmitAsync(new WorkflowStartedEvent
            {
                RunId = state.RunId,
                WorkflowId = workflow.Id,
                EventType = "WorkflowValidationWarning",
                WorkflowName = workflow.Name,
                TotalNodes = workflow.Nodes.Count
            }, CancellationToken.None);
        }

        using var workflowActivity = SpectraActivitySource.StartWorkflow(
            workflow.Id, state.RunId, workflow.Name);

        var nodeMap = workflow.Nodes.ToDictionary(n => n.Id);
        var workflowStopwatch = Stopwatch.StartNew();
        var stepsExecuted = 0;

        var currentNodeId = workflow.EntryNodeId ?? workflow.Nodes.FirstOrDefault()?.Id;

        if (_checkpointStore != null && initialState == null)
        {
            var existingCheckpoint = await _checkpointStore.LoadAsync(
                state.RunId, cancellationToken);

            if (existingCheckpoint is { NextNodeId: not null })
            {
                state = existingCheckpoint.State;
                currentNodeId = existingCheckpoint.NextNodeId;
                stepsExecuted = existingCheckpoint.StepsCompleted;
                state.Status = WorkflowRunStatus.InProgress; // resuming

                await EmitAsync(new WorkflowResumedEvent
                {
                    RunId = state.RunId,
                    WorkflowId = workflow.Id,
                    EventType = nameof(WorkflowResumedEvent),
                    ResumedFromNodeId = existingCheckpoint.NextNodeId,
                    StepsAlreadyCompleted = stepsExecuted
                }, cancellationToken);
            }
        }

        await EmitAsync(new WorkflowStartedEvent
        {
            RunId = state.RunId,
            WorkflowId = workflow.Id,
            EventType = nameof(WorkflowStartedEvent),
            WorkflowName = workflow.Name,
            TotalNodes = workflow.Nodes.Count
        }, cancellationToken);

        var finalStatus = CheckpointStatus.Completed;
        var nodeIterations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hasLoopbacks = workflow.Edges.Any(e => e.IsLoopback);

        while (currentNodeId != null)
        {
            if (!nodeIterations.TryGetValue(currentNodeId, out var count))
                count = 0;
            nodeIterations[currentNodeId] = count + 1;

            if (count > 0)
            {
                if (!hasLoopbacks)
                {
                    state.CurrentNodeId = currentNodeId;
                    state.Errors.Add($"Infinite loop detected at node '{currentNodeId}'");
                    finalStatus = CheckpointStatus.Failed;
                    break;
                }

                if (nodeIterations[currentNodeId] > workflow.MaxNodeIterations)
                {
                    state.CurrentNodeId = currentNodeId;
                    state.Errors.Add($"Node '{currentNodeId}' exceeded maximum iterations ({workflow.MaxNodeIterations})");
                    finalStatus = CheckpointStatus.Failed;
                    break;
                }
            }

            if (!nodeMap.TryGetValue(currentNodeId, out var node))
            {
                state.CurrentNodeId = currentNodeId;
                state.Errors.Add($"Node '{currentNodeId}' not found in workflow");
                finalStatus = CheckpointStatus.Failed;
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            state.CurrentNodeId = node.Id;

            var step = _registry.GetStep(node.StepType);
            if (step == null)
            {
                var error = $"Step type '{node.StepType}' not found in registry";
                state.Errors.Add(error);
                finalStatus = CheckpointStatus.Failed;

                await EmitAsync(new StepCompletedEvent
                {
                    RunId = state.RunId,
                    WorkflowId = workflow.Id,
                    NodeId = node.Id,
                    EventType = nameof(StepCompletedEvent),
                    StepType = node.StepType,
                    Status = StepStatus.Failed,
                    ErrorMessage = error
                }, cancellationToken);
                break;
            }

            var inputs = _stateMapper.ResolveInputs(node, state);

            if (!string.IsNullOrEmpty(node.AgentId) && !inputs.ContainsKey("agentId"))
                inputs["agentId"] = node.AgentId;

            if (!string.IsNullOrEmpty(node.UserPromptRef)
                && !inputs.ContainsKey("userPromptRef")
                && !inputs.ContainsKey("userPrompt"))
            {
                inputs["userPromptRef"] = node.UserPromptRef;
            }

            if (state.Context.TryGetValue("__pendingUserMessage", out var pendingMsg)
                && pendingMsg is string pendingUserMessage
                && !string.IsNullOrEmpty(pendingUserMessage))
            {
                inputs["userMessage"] = pendingUserMessage;
                state.Context.Remove("__pendingUserMessage");
            }

            if (!string.IsNullOrEmpty(node.SubgraphId))
            {
                if (!inputs.ContainsKey("__subgraphId"))
                    inputs["__subgraphId"] = node.SubgraphId;

                if (node.OutputMappings.Count == 0)
                {
                    var subgraphDef = workflow.Subgraphs.FirstOrDefault(s => s.Id == node.SubgraphId);
                    if (subgraphDef != null)
                    {
                        if (subgraphDef.OutputMappings.Count > 0)
                        {
                            foreach (var (_, parentPath) in subgraphDef.OutputMappings)
                                node.OutputMappings[parentPath] = parentPath;
                        }
                        else
                        {
                            node.OutputMappings["childContext"] = "Context.childContext";
                            node.OutputMappings["childArtifacts"] = "Context.childArtifacts";
                        }
                    }
                }
            }

            if (state.Context.TryGetValue("__pendingHandoff", out var handoffObj)
                && handoffObj is Contracts.Workflow.AgentHandoff pendingHandoff
                && node.AgentId is not null
                && node.AgentId.Equals(pendingHandoff.ToAgent, StringComparison.OrdinalIgnoreCase))
            {
                if (pendingHandoff.TransferredMessages is not null)
                    inputs["messages"] = pendingHandoff.TransferredMessages;

                if (!inputs.ContainsKey("userPrompt") && !inputs.ContainsKey("userPromptRef"))
                {
                    var promptParts = new List<string>();
                    if (!string.IsNullOrEmpty(pendingHandoff.Intent))
                        promptParts.Add($"Intent: {pendingHandoff.Intent}");
                    if (pendingHandoff.Payload.TryGetValue("context", out var ctxVal) && ctxVal is not null)
                        promptParts.Add($"Context: {ctxVal}");
                    if (pendingHandoff.Constraints.Count > 0)
                        promptParts.Add($"Constraints: {string.Join("; ", pendingHandoff.Constraints)}");

                    if (promptParts.Count > 0)
                        inputs["userPrompt"] = string.Join("\n", promptParts);
                }

                if (state.Context.TryGetValue("__agentExecutionContext", out var execCtxObj))
                    inputs["__agentExecutionContext"] = execCtxObj;

                state.Context.Remove("__pendingHandoff");
            }

            await EmitAsync(new StepStartedEvent
            {
                RunId = state.RunId,
                WorkflowId = workflow.Id,
                NodeId = node.Id,
                EventType = nameof(StepStartedEvent),
                StepType = node.StepType,
                Inputs = inputs
            }, cancellationToken);

            // ── Declarative interrupt: BEFORE step execution ──
            if (!string.IsNullOrEmpty(node.InterruptBefore))
            {
                var interruptRequest = new InterruptRequest
                {
                    RunId = state.RunId,
                    WorkflowId = workflow.Id,
                    NodeId = node.Id,
                    Reason = node.InterruptBefore,
                    Title = $"Interrupt before '{node.Id}'"
                };

                var resolved = await TryResolveInterruptAsync(interruptRequest, cancellationToken);
                var outcome = ResolveInterruptOutcome(resolved);

                if (outcome.Terminal is { } terminalStatus)
                {
                    if (outcome.ErrorMessage is not null)
                        state.Errors.Add(outcome.ErrorMessage);

                    await EmitAsync(new StepInterruptedEvent
                    {
                        RunId = state.RunId,
                        WorkflowId = workflow.Id,
                        NodeId = node.Id,
                        EventType = nameof(StepInterruptedEvent),
                        StepType = node.StepType,
                        Reason = outcome.EventReason ?? node.InterruptBefore,
                        IsDeclarative = true
                    }, cancellationToken);

                    finalStatus = terminalStatus;
                    await SaveCheckpointAsync(
                        workflow, state, null, node.Id,
                        stepsExecuted, finalStatus, cancellationToken);
                    break;
                }

                if (!outcome.Proceed)
                {
                    await EmitAsync(new StepInterruptedEvent
                    {
                        RunId = state.RunId,
                        WorkflowId = workflow.Id,
                        NodeId = node.Id,
                        EventType = nameof(StepInterruptedEvent),
                        StepType = node.StepType,
                        Reason = node.InterruptBefore,
                        IsDeclarative = true
                    }, cancellationToken);

                    finalStatus = CheckpointStatus.Interrupted;
                    if (_checkpointOptions.CheckpointOnInterrupt)
                    {
                        await SaveCheckpointAsync(
                            workflow, state, null, node.Id,
                            stepsExecuted, finalStatus, cancellationToken,
                            pendingInterrupt: interruptRequest);
                    }
                    break;
                }
            }

            var tokenIndex = 0;
            var context = new StepContext
            {
                RunId = state.RunId,
                WorkflowId = workflow.Id,
                NodeId = node.Id,
                State = state,
                CancellationToken = cancellationToken,
                Inputs = inputs,
                Services = _services,
                WorkflowDefinition = workflow,
                Memory = _memoryStore,
                RunContext = _runContext,
                Interrupt = _interruptHandler != null
                    ? (request, ct) => _interruptHandler.HandleAsync(request, ct)
                    : null,
                OnToken = _streamingChannel != null
                    ? async (token, ct) =>
                    {
                        var tokenEvent = new TokenStreamEvent
                        {
                            RunId = state.RunId,
                            WorkflowId = workflow.Id,
                            NodeId = node.Id,
                            EventType = nameof(TokenStreamEvent),
                            Token = token,
                            TokenIndex = tokenIndex++
                        };
                        await EmitAsync(tokenEvent, ct);
                    }
                : null
            };

            var stepStopwatch = Stopwatch.StartNew();
            using var stepActivity = SpectraActivitySource.StartStep(
                workflow.Id, state.RunId, node.Id, node.StepType);

            StepResult result;
            try
            {
                result = await step.ExecuteAsync(context);
            }
            catch (InterruptException ex)
            {
                stepStopwatch.Stop();
                stepsExecuted++;

                stepActivity?.SetTag(SpectraTags.StepStatus, "interrupted");
                stepActivity?.SetTag(SpectraTags.InterruptReason,
                    ex.Request.Reason ?? "Programmatic interrupt");

                await EmitAsync(new StepInterruptedEvent
                {
                    RunId = state.RunId,
                    WorkflowId = workflow.Id,
                    NodeId = node.Id,
                    EventType = nameof(StepInterruptedEvent),
                    StepType = node.StepType,
                    Reason = ex.Request.Reason ?? "Programmatic interrupt",
                    InterruptTitle = ex.Request.Title,
                    IsDeclarative = false
                }, cancellationToken);

                finalStatus = CheckpointStatus.Interrupted;
                if (_checkpointOptions.CheckpointOnInterrupt)
                {
                    await SaveCheckpointAsync(
                        workflow, state, null, node.Id,
                        stepsExecuted - 1, finalStatus, cancellationToken,
                        pendingInterrupt: ex.Request);
                }
                break;
            }
            stepStopwatch.Stop();
            stepsExecuted++;

            stepActivity?.SetTag(SpectraTags.StepStatus, result.Status.ToString());
            if (result.Status == StepStatus.Failed && result.ErrorMessage is not null)
                SpectraActivitySource.RecordError(stepActivity, result.ErrorMessage);

            await EmitAsync(new StepCompletedEvent
            {
                RunId = state.RunId,
                WorkflowId = workflow.Id,
                NodeId = node.Id,
                EventType = nameof(StepCompletedEvent),
                StepType = node.StepType,
                Status = result.Status,
                Duration = stepStopwatch.Elapsed,
                Outputs = result.Outputs,
                ErrorMessage = result.ErrorMessage
            }, cancellationToken);

            if (result.Status == StepStatus.Failed)
            {
                state.Errors.Add(result.ErrorMessage ?? "Unknown error");
                finalStatus = CheckpointStatus.Failed;
                if (_checkpointOptions.CheckpointOnFailure)
                {
                    await SaveCheckpointAsync(
                        workflow, state, node.Id, null,
                        stepsExecuted, finalStatus, cancellationToken);
                }
                break;
            }

            if (result.Status == StepStatus.NeedsContinuation)
            {
                finalStatus = CheckpointStatus.InProgress;
                if (_checkpointOptions.CheckpointOnContinuation)
                {
                    await SaveCheckpointAsync(
                        workflow, state, null, node.Id,
                        stepsExecuted - 1, finalStatus, cancellationToken);
                }
                break;
            }

            if (result.Status == StepStatus.AwaitingInput)
            {
                finalStatus = CheckpointStatus.AwaitingInput;
                if (_checkpointOptions.CheckpointOnContinuation)
                {
                    await SaveCheckpointAsync(
                        workflow, state, null, node.Id,
                        stepsExecuted, finalStatus, cancellationToken);
                }
                break;
            }

            if (result.Status == StepStatus.Interrupted)
            {
                finalStatus = CheckpointStatus.Interrupted;

                await EmitAsync(new StepInterruptedEvent
                {
                    RunId = state.RunId,
                    WorkflowId = workflow.Id,
                    NodeId = node.Id,
                    EventType = nameof(StepInterruptedEvent),
                    StepType = node.StepType,
                    Reason = result.ErrorMessage ?? "Step returned Interrupted",
                    IsDeclarative = false
                }, cancellationToken);

                if (_checkpointOptions.CheckpointOnInterrupt)
                {
                    await SaveCheckpointAsync(
                        workflow, state, null, node.Id,
                        stepsExecuted - 1, finalStatus, cancellationToken);
                }
                break;
            }

            // ── Handoff ──
            if (result.Status == StepStatus.Handoff && result.Handoff is not null)
            {
                var handoff = result.Handoff;
                var targetNodeId = ResolveHandoffTarget(workflow, handoff.ToAgent);

                if (targetNodeId is null)
                {
                    state.Errors.Add($"Handoff target agent '{handoff.ToAgent}' has no corresponding agent node in the workflow.");
                    finalStatus = CheckpointStatus.Failed;
                    break;
                }

                if (result.Outputs.TryGetValue(
                    "__agentExecutionContext", out var execCtxObj) && execCtxObj is not null)
                {
                    state.Context["__agentExecutionContext"] = execCtxObj;
                }

                state.Context["__pendingHandoff"] = handoff;

                await ApplyOutputsWithEvents(
                    workflow, node, state, result.Outputs, cancellationToken);

                var sourceAgent = workflow.Agents.FirstOrDefault(a => a.Id == handoff.FromAgent);
                if (sourceAgent?.HandoffPolicy == Contracts.Workflow.HandoffPolicy.RequiresApproval)
                {
                    var interruptRequest = new InterruptRequest
                    {
                        RunId = state.RunId,
                        WorkflowId = workflow.Id,
                        NodeId = node.Id,
                        Reason = $"Handoff from '{handoff.FromAgent}' to '{handoff.ToAgent}' requires approval",
                        Title = $"Approve handoff to '{handoff.ToAgent}'?"
                    };

                    var resolved = await TryResolveInterruptAsync(interruptRequest, cancellationToken);
                    var outcome = ResolveInterruptOutcome(resolved);

                    if (outcome.Terminal is { } handoffTerminalStatus)
                    {
                        if (outcome.ErrorMessage is not null)
                            state.Errors.Add(outcome.ErrorMessage);

                        await EmitAsync(new StepInterruptedEvent
                        {
                            RunId = state.RunId,
                            WorkflowId = workflow.Id,
                            NodeId = node.Id,
                            EventType = nameof(StepInterruptedEvent),
                            StepType = node.StepType,
                            Reason = outcome.EventReason ?? interruptRequest.Reason,
                            IsDeclarative = false
                        }, cancellationToken);

                        finalStatus = handoffTerminalStatus;
                        await SaveCheckpointAsync(
                            workflow, state, node.Id, null,
                            stepsExecuted, finalStatus, cancellationToken);
                        break;
                    }

                    if (!outcome.Proceed)
                    {
                        await EmitAsync(new StepInterruptedEvent
                        {
                            RunId = state.RunId,
                            WorkflowId = workflow.Id,
                            NodeId = node.Id,
                            EventType = nameof(StepInterruptedEvent),
                            StepType = node.StepType,
                            Reason = interruptRequest.Reason,
                            IsDeclarative = false
                        }, cancellationToken);

                        finalStatus = CheckpointStatus.Interrupted;
                        if (_checkpointOptions.CheckpointOnInterrupt)
                        {
                            await SaveCheckpointAsync(
                                workflow, state, node.Id, targetNodeId,
                                stepsExecuted, finalStatus, cancellationToken,
                                pendingInterrupt: interruptRequest);
                        }
                        break;
                    }
                }

                if (ShouldCheckpoint(finalStatus: CheckpointStatus.InProgress))
                {
                    await SaveCheckpointAsync(
                        workflow, state, node.Id, targetNodeId, stepsExecuted,
                        CheckpointStatus.InProgress, cancellationToken);
                }

                currentNodeId = targetNodeId;
                continue;
            }

            // ── Declarative interrupt: AFTER step execution ──
            if (!string.IsNullOrEmpty(node.InterruptAfter))
            {
                var interruptRequest = new InterruptRequest
                {
                    RunId = state.RunId,
                    WorkflowId = workflow.Id,
                    NodeId = node.Id,
                    Reason = node.InterruptAfter,
                    Title = $"Interrupt after '{node.Id}'"
                };

                var resolved = await TryResolveInterruptAsync(interruptRequest, cancellationToken);
                var outcome = ResolveInterruptOutcome(resolved);

                if (outcome.Terminal is { } afterTerminalStatus)
                {
                    await ApplyOutputsWithEvents(
                        workflow, node, state, result.Outputs, cancellationToken);

                    if (outcome.ErrorMessage is not null)
                        state.Errors.Add(outcome.ErrorMessage);

                    await EmitAsync(new StepInterruptedEvent
                    {
                        RunId = state.RunId,
                        WorkflowId = workflow.Id,
                        NodeId = node.Id,
                        EventType = nameof(StepInterruptedEvent),
                        StepType = node.StepType,
                        Reason = outcome.EventReason ?? node.InterruptAfter,
                        IsDeclarative = true
                    }, cancellationToken);

                    finalStatus = afterTerminalStatus;
                    await SaveCheckpointAsync(
                        workflow, state, node.Id, null,
                        stepsExecuted, finalStatus, cancellationToken);
                    break;
                }

                if (!outcome.Proceed)
                {
                    await ApplyOutputsWithEvents(
                        workflow, node, state, result.Outputs, cancellationToken);

                    await EmitAsync(new StepInterruptedEvent
                    {
                        RunId = state.RunId,
                        WorkflowId = workflow.Id,
                        NodeId = node.Id,
                        EventType = nameof(StepInterruptedEvent),
                        StepType = node.StepType,
                        Reason = node.InterruptAfter,
                        IsDeclarative = true
                    }, cancellationToken);

                    var nextAfterInterrupt = ResolveNextNode(
                        workflow, node.Id, state, cancellationToken);

                    finalStatus = CheckpointStatus.Interrupted;
                    if (_checkpointOptions.CheckpointOnInterrupt)
                    {
                        await SaveCheckpointAsync(
                            workflow, state, node.Id, nextAfterInterrupt,
                            stepsExecuted, finalStatus, cancellationToken,
                            pendingInterrupt: interruptRequest);
                    }
                    break;
                }
            }

            await ApplyOutputsWithEvents(
                workflow, node, state, result.Outputs, cancellationToken);

            var nextNodeId = ResolveNextNode(
                workflow, node.Id, state, cancellationToken);

            if (ShouldCheckpoint(finalStatus: nextNodeId == null ? CheckpointStatus.Completed : CheckpointStatus.InProgress))
            {
                await SaveCheckpointAsync(
                    workflow, state, node.Id, nextNodeId, stepsExecuted,
                    nextNodeId == null ? CheckpointStatus.Completed : CheckpointStatus.InProgress,
                    cancellationToken);
            }

            currentNodeId = nextNodeId;
        }

        workflowStopwatch.Stop();

        workflowActivity?.SetTag(SpectraTags.StepsExecuted, stepsExecuted);
        if (state.Errors.Count > 0)
            workflowActivity?.SetStatus(ActivityStatusCode.Error, string.Join("; ", state.Errors));
        else
            workflowActivity?.SetStatus(ActivityStatusCode.Ok);

        if (finalStatus is CheckpointStatus.Completed or CheckpointStatus.Failed or CheckpointStatus.Cancelled)
        {
            await SaveCheckpointAsync(
                workflow, state, state.CurrentNodeId, null,
                stepsExecuted, finalStatus, cancellationToken);
        }

        // Single finalization point — covers every break path above.
        state.Status = ToRunStatus(finalStatus);

        await EmitAsync(new WorkflowCompletedEvent
        {
            RunId = state.RunId,
            WorkflowId = workflow.Id,
            EventType = nameof(WorkflowCompletedEvent),
            Success = state.Status == WorkflowRunStatus.Completed,
            Duration = workflowStopwatch.Elapsed,
            StepsExecuted = stepsExecuted,
            Errors = state.Errors.ToList()
        }, cancellationToken);

        return state;
    }

    /// <summary>
    /// Converts the internal <see cref="CheckpointStatus"/> used by the run loop
    /// into the public <see cref="WorkflowRunStatus"/> surfaced on <see cref="WorkflowState"/>.
    /// </summary>
    private static WorkflowRunStatus ToRunStatus(CheckpointStatus status) => status switch
    {
        CheckpointStatus.InProgress => WorkflowRunStatus.InProgress,
        CheckpointStatus.Completed => WorkflowRunStatus.Completed,
        CheckpointStatus.Failed => WorkflowRunStatus.Failed,
        CheckpointStatus.Interrupted => WorkflowRunStatus.Interrupted,
        CheckpointStatus.AwaitingInput => WorkflowRunStatus.AwaitingInput,
        CheckpointStatus.Cancelled => WorkflowRunStatus.Cancelled,
        _ => WorkflowRunStatus.InProgress
    };

    /// <summary>
    /// Maps an <see cref="InterruptResponse"/> (or its absence) to a runner-level decision.
    /// </summary>
    private static InterruptOutcome ResolveInterruptOutcome(InterruptResponse? response)
    {
        if (response is null)
            return InterruptOutcome.Park;

        return response.Status switch
        {
            InterruptStatus.Approved => InterruptOutcome.ProceedResult,

            InterruptStatus.Rejected => new InterruptOutcome(
                Proceed: false,
                Terminal: CheckpointStatus.Failed,
                ErrorMessage: BuildInterruptError("rejected", response),
                EventReason: "Interrupt rejected"),

            InterruptStatus.TimedOut => new InterruptOutcome(
                Proceed: false,
                Terminal: CheckpointStatus.Failed,
                ErrorMessage: BuildInterruptError("timed out", response),
                EventReason: "Interrupt timed out"),

            InterruptStatus.Cancelled => new InterruptOutcome(
                Proceed: false,
                Terminal: CheckpointStatus.Cancelled,
                ErrorMessage: null,
                EventReason: "Interrupt cancelled"),

            _ => InterruptOutcome.Park
        };
    }

    private static string BuildInterruptError(string verb, InterruptResponse response)
    {
        var parts = new List<string> { $"Interrupt {verb}" };
        if (!string.IsNullOrWhiteSpace(response.RespondedBy))
            parts.Add($"by {response.RespondedBy}");
        if (!string.IsNullOrWhiteSpace(response.Comment))
            parts.Add($"({response.Comment})");
        return string.Join(" ", parts);
    }

    private readonly record struct InterruptOutcome(
        bool Proceed,
        CheckpointStatus? Terminal,
        string? ErrorMessage,
        string? EventReason)
    {
        public static readonly InterruptOutcome Park = new(false, null, null, null);
        public static readonly InterruptOutcome ProceedResult = new(true, null, null, null);
    }

    private string? ResolveNextNode(
        WorkflowDefinition workflow,
        string currentNodeId,
        WorkflowState state,
        CancellationToken cancellationToken)
    {
        var outgoingEdges = workflow.Edges
            .Where(e => e.From == currentNodeId)
            .ToList();

        if (outgoingEdges.Count == 0) return null;

        foreach (var edge in outgoingEdges)
        {
            if (string.IsNullOrEmpty(edge.Condition))
            {
                EmitAsync(new BranchEvaluatedEvent
                {
                    RunId = state.RunId,
                    WorkflowId = workflow.Id,
                    EventType = nameof(BranchEvaluatedEvent),
                    FromNodeId = currentNodeId,
                    ToNodeId = edge.To,
                    Condition = "(default)",
                    Result = true
                }, cancellationToken).GetAwaiter().GetResult();

                return edge.To;
            }

            var result = _conditionEvaluator.Evaluate(edge.Condition, state);

            EmitAsync(new BranchEvaluatedEvent
            {
                RunId = state.RunId,
                WorkflowId = workflow.Id,
                EventType = nameof(BranchEvaluatedEvent),
                FromNodeId = currentNodeId,
                ToNodeId = edge.To,
                Condition = edge.Condition,
                Result = result.Satisfied,
                Reason = result.Reason
            }, cancellationToken).GetAwaiter().GetResult();

            if (result.Satisfied) return edge.To;
        }

        var defaultEdge = outgoingEdges
            .FirstOrDefault(e => string.IsNullOrEmpty(e.Condition));
        return defaultEdge?.To;
    }

    public async Task<WorkflowState> ResumeAsync(
        WorkflowDefinition workflow,
        string runId,
        CancellationToken cancellationToken = default)
    {
        if (_checkpointStore == null)
            throw new InvalidOperationException("No checkpoint store configured");

        var checkpoint = await _checkpointStore.LoadAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException($"No checkpoint found for run {runId}");

        if (checkpoint.Status == CheckpointStatus.Completed)
            throw new InvalidOperationException($"Run {runId} has already completed and cannot be resumed.");

        if (checkpoint.Status == CheckpointStatus.Cancelled)
            throw new InvalidOperationException($"Run {runId} was cancelled and cannot be resumed.");

        var state = checkpoint.State;
        state.RunId = runId;

        if (checkpoint.NextNodeId != null)
            state.CurrentNodeId = checkpoint.NextNodeId;

        return await RunAsync(workflow, state, cancellationToken);
    }

    private InterruptResponse? _pendingInterruptResponse;

    public async Task<WorkflowState> ResumeWithResponseAsync(
        WorkflowDefinition workflow,
        string runId,
        InterruptResponse interruptResponse,
        CancellationToken cancellationToken = default)
    {
        if (_checkpointStore == null)
            throw new InvalidOperationException("No checkpoint store configured");

        var checkpoint = await _checkpointStore.LoadAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException($"No checkpoint found for run {runId}");

        if (checkpoint.Status != CheckpointStatus.Interrupted)
            throw new InvalidOperationException(
                $"Run {runId} is not in an interrupted state (current: {checkpoint.Status}).");

        _pendingInterruptResponse = interruptResponse;

        try
        {
            var state = checkpoint.State;
            state.RunId = runId;

            if (checkpoint.NextNodeId != null)
                state.CurrentNodeId = checkpoint.NextNodeId;

            return await RunAsync(workflow, state, cancellationToken);
        }
        finally
        {
            _pendingInterruptResponse = null;
        }
    }

    private Task<InterruptResponse?> TryResolveInterruptAsync(
        InterruptRequest request, CancellationToken cancellationToken)
    {
        if (_pendingInterruptResponse != null)
        {
            var response = _pendingInterruptResponse;
            _pendingInterruptResponse = null;
            return Task.FromResult<InterruptResponse?>(response);
        }

        if (_interruptHandler != null)
            return Task.FromResult<InterruptResponse?>(null);

        return Task.FromResult<InterruptResponse?>(null);
    }

    public async Task<WorkflowState> ResumeFromCheckpointAsync(
        WorkflowDefinition workflow,
        string runId,
        int checkpointIndex,
        CancellationToken cancellationToken = default)
    {
        if (_checkpointStore == null)
            throw new InvalidOperationException("No checkpoint store configured");

        var checkpoint = await _checkpointStore.LoadByIndexAsync(runId, checkpointIndex, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No checkpoint at index {checkpointIndex} for run '{runId}'.");

        var state = checkpoint.State;
        state.RunId = runId;

        if (checkpoint.NextNodeId != null)
            state.CurrentNodeId = checkpoint.NextNodeId;

        return await RunAsync(workflow, state, cancellationToken);
    }

    public async Task<WorkflowState> ForkAndRunAsync(
        WorkflowDefinition workflow,
        string sourceRunId,
        int checkpointIndex,
        string? newRunId = null,
        WorkflowState? stateOverrides = null,
        CancellationToken cancellationToken = default)
    {
        if (_checkpointStore == null)
            throw new InvalidOperationException("No checkpoint store configured");

        var forkRunId = newRunId ?? Guid.NewGuid().ToString();

        var forkedCheckpoint = await _checkpointStore.ForkAsync(
            sourceRunId, checkpointIndex, forkRunId, stateOverrides, cancellationToken);

        await EmitAsync(new WorkflowForkedEvent
        {
            RunId = forkRunId,
            WorkflowId = workflow.Id,
            EventType = nameof(WorkflowForkedEvent),
            SourceRunId = sourceRunId,
            SourceCheckpointIndex = checkpointIndex
        }, cancellationToken);

        var state = forkedCheckpoint.State;

        if (forkedCheckpoint.NextNodeId != null)
            state.CurrentNodeId = forkedCheckpoint.NextNodeId;

        return await RunAsync(workflow, state, cancellationToken);
    }

    public async Task<WorkflowState> SendMessageAsync(
        WorkflowDefinition workflow,
        string runId,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        if (_checkpointStore == null)
            throw new InvalidOperationException("No checkpoint store configured");

        var checkpoint = await _checkpointStore.LoadAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException($"No checkpoint found for run {runId}");

        if (checkpoint.Status != CheckpointStatus.AwaitingInput)
            throw new InvalidOperationException(
                $"Run {runId} is not awaiting input (current: {checkpoint.Status}).");

        var state = checkpoint.State;
        state.RunId = runId;
        state.Context["__pendingUserMessage"] = userMessage;

        if (checkpoint.NextNodeId != null)
            state.CurrentNodeId = checkpoint.NextNodeId;

        return await RunAsync(workflow, state, cancellationToken);
    }


    private async Task SaveCheckpointAsync(
        WorkflowDefinition workflow,
        WorkflowState state,
        string? lastCompletedNodeId,
        string? nextNodeId,
        int stepsCompleted,
        CheckpointStatus status,
        CancellationToken cancellationToken,
        InterruptRequest? pendingInterrupt = null)
    {
        if (_checkpointStore == null) return;

        var checkpoint = new Checkpoint
        {
            RunId = state.RunId,
            WorkflowId = workflow.Id,
            State = state,
            LastCompletedNodeId = lastCompletedNodeId,
            NextNodeId = nextNodeId,
            StepsCompleted = stepsCompleted,
            Status = status,
            UpdatedAt = DateTimeOffset.UtcNow,
            PendingInterrupt = pendingInterrupt,
            TenantId = _runContext.TenantId,
            UserId = _runContext.UserId
        };

        await _checkpointStore.SaveAsync(checkpoint, cancellationToken);
    }

    private bool ShouldCheckpoint(CheckpointStatus finalStatus)
    {
        if (_checkpointStore == null) return false;

        return _checkpointOptions.Frequency switch
        {
            CheckpointFrequency.EveryNode => true,
            CheckpointFrequency.StatusChangeOnly =>
                finalStatus is CheckpointStatus.Completed or CheckpointStatus.Failed or CheckpointStatus.Cancelled,
            CheckpointFrequency.Disabled => false,
            _ => true
        };
    }

    private async Task ApplyOutputsWithEvents(
        WorkflowDefinition workflow,
        NodeDefinition node,
        WorkflowState state,
        Dictionary<string, object?> outputs,
        CancellationToken cancellationToken)
    {
        foreach (var mapping in node.OutputMappings)
        {
            if (outputs.TryGetValue(mapping.Key, out var value))
            {
                var parts = mapping.Value.Split('.', 2);
                var section = parts[0];
                var key = parts.Length > 1 ? parts[1] : mapping.Key;

                await EmitAsync(new StateChangedEvent
                {
                    RunId = state.RunId,
                    WorkflowId = workflow.Id,
                    NodeId = node.Id,
                    EventType = nameof(StateChangedEvent),
                    Section = section,
                    Key = key,
                    Value = value
                }, cancellationToken);
            }
        }

        if (node.OutputMappings.Count == 0 && outputs.Count > 0)
        {
            await EmitAsync(new StateChangedEvent
            {
                RunId = state.RunId,
                WorkflowId = workflow.Id,
                NodeId = node.Id,
                EventType = nameof(StateChangedEvent),
                Section = "Context",
                Key = node.Id,
                Value = outputs
            }, cancellationToken);
        }

        _stateMapper.ApplyOutputs(node, state, outputs);

        // Always mirror node outputs to state.Nodes so {{nodes.X.Y}} resolves
        // regardless of whether the node has output mappings.
        state.Nodes[node.Id] = outputs;
    }

    private static string? ResolveHandoffTarget(WorkflowDefinition workflow, string targetAgentId)
    {
        var targetNode = workflow.Nodes.FirstOrDefault(n =>
            n.AgentId is not null &&
            n.AgentId.Equals(targetAgentId, StringComparison.OrdinalIgnoreCase));

        return targetNode?.Id;
    }

    private async Task EmitAsync(WorkflowEvent evt, CancellationToken cancellationToken)
    {
        evt = evt with
        {
            TenantId = evt.TenantId ?? _runContext.TenantId,
            UserId = evt.UserId ?? _runContext.UserId
        };

        if (_eventSink != null)
            await _eventSink.PublishAsync(evt, cancellationToken);
    }
}

internal class DefaultConditionEvaluator : IConditionEvaluator
{
    public ConditionResult Evaluate(string expression, WorkflowState state)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return ConditionResult.True();

        return ConditionResult.False($"No condition evaluator configured for: {expression}");
    }
}