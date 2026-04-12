using Spectra.Contracts.Memory;
using Spectra.Contracts.Interrupts;
using System.Diagnostics;
using System.Text.Json;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Prompts;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Providers.Fallback;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Tools;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Prompts;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Built-in step that runs an autonomous tool-using agent loop.
/// The LLM is called iteratively: when it returns tool calls, the tools are
/// executed in parallel and the results fed back. The loop ends when the LLM
/// produces a final response with no tool calls, or a guard limit is reached.
/// </summary>
public class AgentStep : IStep
{
    private readonly IProviderRegistry _providerRegistry;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IToolRegistry _toolRegistry;
    private readonly PromptRenderer _promptRenderer;
    private readonly IPromptRegistry? _promptRegistry;
    private readonly IEventSink? _eventSink;
    private readonly IMemoryStore? _memoryStore;
    private readonly MemoryOptions? _memoryOptions;
    private readonly IFallbackPolicyRegistry? _fallbackPolicyRegistry;

    public string StepType => "agent";

    public AgentStep(
        IProviderRegistry providerRegistry,
        IAgentRegistry agentRegistry,
        IToolRegistry toolRegistry,
        PromptRenderer promptRenderer,
        IPromptRegistry? promptRegistry = null,
        IEventSink? eventSink = null,
        IMemoryStore? memoryStore = null,
        MemoryOptions? memoryOptions = null,
        IFallbackPolicyRegistry? fallbackPolicyRegistry = null)
    {
        _providerRegistry = providerRegistry;
        _agentRegistry = agentRegistry;
        _toolRegistry = toolRegistry;
        _promptRenderer = promptRenderer;
        _promptRegistry = promptRegistry;
        _eventSink = eventSink;
        _memoryStore = memoryStore;
        _memoryOptions = memoryOptions;
        _fallbackPolicyRegistry = fallbackPolicyRegistry;
    }

    public async Task<StepResult> ExecuteAsync(StepContext context)
    {
        // 1. Resolve LLM client
        var (client, clientError) = LlmClientResolver.ResolveClientWithFallback(
            context, _agentRegistry, _providerRegistry, _fallbackPolicyRegistry, _eventSink);

        if (client is null)
            return StepResult.Fail(clientError ?? "Failed to resolve LLM client.");

        // 2. Resolve agent and system prompt
        var agent = LlmClientResolver.TryGetAgent(context, _agentRegistry);
        var systemPrompt = LlmClientResolver.ResolveSystemPrompt(
            context, agent, _promptRegistry, _promptRenderer);

        // 3. Resolve tools (explicit whitelist required)
        var (toolDefinitions, tools, toolError) = ResolveTools(context);
        if (toolError is not null)
            return StepResult.Fail(toolError);

        // 4. Build initial message list
        var messages = BuildInitialMessages(context);

        // 5. Read guard parameters
        var maxIterations = LlmClientResolver.GetIntInput(context, "maxIterations", 10);
        var tokenBudget = LlmClientResolver.GetIntInput(context, "tokenBudget", 0);
        var outputSchema = LlmClientResolver.GetStringInput(context, "outputSchema");

        // 6. Run the loop
        var model = agent?.Model
                    ?? LlmClientResolver.GetStringInput(context, "model")
                    ?? "unknown";

        var temperature = LlmClientResolver.GetDoubleInput(
            context, "temperature", agent?.Temperature ?? 0.7);
        var maxTokens = LlmClientResolver.GetIntInput(
            context, "maxTokens", agent?.MaxTokens ?? 2048);

        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var iteration = 0;
        string stopReason = "max_iterations";

        try
        {
            while (iteration < maxIterations)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                iteration++;

                // Determine if this might be the final call (no tool calls expected)
                // We can't know in advance, so structured output is applied optimistically
                // only when tools are exhausted or not provided. In practice, we always
                // send tools and let the LLM decide. If the response has no tool calls
                // AND an outputSchema is set, we validate after.
                var request = new LlmRequest
                {
                    Model = model,
                    Messages = messages.ToList(),
                    SystemPrompt = systemPrompt,
                    Temperature = temperature,
                    MaxTokens = maxTokens,
                    Tools = toolDefinitions.Count > 0 ? toolDefinitions : null,
                    SkipCache = true
                };

                // Execute LLM call (with streaming if available)
                LlmResponse response;

                if (context.IsStreaming && client is ILlmStreamClient streamClient && toolDefinitions.Count == 0)
                {
                    response = await ExecuteStreamingAsync(
                        streamClient, request, context.OnToken!, context.CancellationToken);
                }
                else
                {
                    response = await client.CompleteAsync(request, context.CancellationToken);
                }

                if (!response.Success)
                    return StepResult.Fail(response.ErrorMessage ?? $"LLM request failed at iteration {iteration}.");

                // Track tokens
                totalInputTokens += response.InputTokens ?? 0;
                totalOutputTokens += response.OutputTokens ?? 0;

                // TODO: Token budget gating — when cost-tracking ticket is implemented,
                // enforce tokenBudget here. For now we track but don't gate.
                // if (tokenBudget > 0 && (totalInputTokens + totalOutputTokens) > tokenBudget)
                // {
                //     stopReason = "token_budget_exceeded";
                //     break;
                // }

                if (response.HasToolCalls)
                {
                    // ── Check for handoff interception ──
                    var transferCall = response.ToolCalls!.FirstOrDefault(
                        tc => tc.Name.Equals("transfer_to_agent", StringComparison.OrdinalIgnoreCase));

                    if (transferCall is not null)
                    {
                        var handoffResult = HandleHandoffRequest(
                            context, agent, transferCall, messages, iteration,
                            totalInputTokens + (response.InputTokens ?? 0),
                            totalOutputTokens + (response.OutputTokens ?? 0),
                            model);

                        if (handoffResult is not null)
                            return handoffResult;

                        // If handoff was blocked, the error was added as a tool result
                        // and we continue the loop (the LLM will see the error)
                    }

                    // Append assistant message with tool calls
                    messages.Add(new LlmMessage
                    {
                        Role = LlmRole.Assistant,
                        Content = response.Content,
                        ToolCalls = response.ToolCalls
                    });

                    // Execute all tool calls in parallel (excluding intercepted transfer)
                    var callsToExecute = response.ToolCalls!
                        .Where(tc => !tc.Name.Equals("transfer_to_agent", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (callsToExecute.Count > 0)
                    {
                        var toolResults = await ExecuteToolCallsAsync(
                            callsToExecute, tools, context, iteration);

                        foreach (var (toolCall, toolResult) in toolResults)
                        {
                            var resultContent = toolResult.Success
                                ? toolResult.Content ?? string.Empty
                                : $"Error: {toolResult.Error ?? "Tool execution failed."}";

                            messages.Add(LlmMessage.ToolResult(toolCall.Id, resultContent));
                        }
                    }

                    // Emit iteration event
                    // Emit iteration event
                    await EmitEventAsync(new AgentIterationEvent
                    {
                        RunId = context.RunId,
                        WorkflowId = context.WorkflowId,
                        NodeId = context.NodeId,
                        EventType = "agent.iteration",
                        Iteration = iteration,
                        ToolCallCount = response.ToolCalls!.Count,
                        ToolNames = response.ToolCalls!.Select(tc => tc.Name).ToList(),
                        InputTokens = response.InputTokens,
                        OutputTokens = response.OutputTokens
                    }, context);
                }
                else
                {
                    // Final response — no tool calls
                    messages.Add(LlmMessage.FromText(LlmRole.Assistant, response.Content));
                    stopReason = response.StopReason ?? "end_turn";

                    // Validate structured output if schema was requested
                    var finalContent = response.Content;
                    Dictionary<string, object?>? parsedOutput = null;

                    if (!string.IsNullOrEmpty(outputSchema))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(finalContent);
                            parsedOutput = new Dictionary<string, object?>
                            {
                                ["parsedResponse"] = doc.RootElement.Clone()
                            };
                        }
                        catch (JsonException ex)
                        {
                            return StepResult.Fail(
                                $"Agent final response is not valid JSON: {ex.Message}",
                                ex,
                                BuildOutputs(finalContent, messages, iteration,
                                    totalInputTokens, totalOutputTokens, model, stopReason));
                        }
                    }

                    // Emit completed event
                    // Emit completed event
                    await EmitEventAsync(new AgentCompletedEvent
                    {
                        RunId = context.RunId,
                        WorkflowId = context.WorkflowId,
                        NodeId = context.NodeId,
                        EventType = "agent.completed",
                        TotalIterations = iteration,
                        TotalInputTokens = totalInputTokens,
                        TotalOutputTokens = totalOutputTokens,
                        StopReason = stopReason
                    }, context);

                    var outputs = BuildOutputs(finalContent, messages, iteration,
                        totalInputTokens, totalOutputTokens, model, stopReason);

                    if (parsedOutput is not null)
                    {
                        foreach (var kv in parsedOutput)
                            outputs[kv.Key] = kv.Value;
                    }

                    return StepResult.Success(outputs);
                }
            }

            // Guard limit reached — check for escalation target
            var lastAssistant = messages.LastOrDefault(m => m.Role == LlmRole.Assistant);

            if (agent?.EscalationTarget is not null)
            {
                var escalationResult = await HandleEscalationAsync(
                    context, agent, "max_iterations",
                    $"Agent '{agent.Id}' reached max iterations ({maxIterations}) without completing.",
                    messages, iteration, totalInputTokens, totalOutputTokens, model);

                if (escalationResult is not null)
                    return escalationResult;
            }

            await EmitEventAsync(new AgentCompletedEvent
            {
                RunId = context.RunId,
                WorkflowId = context.WorkflowId,
                NodeId = context.NodeId,
                EventType = "agent.completed",
                TotalIterations = iteration,
                TotalInputTokens = totalInputTokens,
                TotalOutputTokens = totalOutputTokens,
                StopReason = stopReason
            }, context);

            return StepResult.Success(BuildOutputs(
                lastAssistant?.Content ?? string.Empty, messages, iteration,
                totalInputTokens, totalOutputTokens, model, stopReason));
        }
        catch (OperationCanceledException)
        {
            return StepResult.Fail("Agent step was cancelled.");
        }
        catch (Contracts.Interrupts.InterruptException)
        {
            // Re-throw interrupt exceptions so the runner can checkpoint and suspend
            throw;
        }
        catch (Exception ex)
        {
            return StepResult.Fail($"Agent step failed: {ex.Message}", ex);
        }
    }

    private (List<ToolDefinition> Definitions, Dictionary<string, ITool> Tools, string? Error) ResolveTools(
        StepContext context)
    {
        var definitions = new List<ToolDefinition>();
        var tools = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);

        // Tools must be explicitly listed (whitelist)
        if (context.Inputs.TryGetValue("tools", out var toolsObj) && toolsObj is not null)
        {
            var toolNames = toolsObj switch
            {
                IEnumerable<string> names => names.ToList(),
                IEnumerable<object> objects => objects.Select(o => o?.ToString() ?? "").Where(s => s.Length > 0)
                    .ToList(),
                string single => [single],
                _ => []
            };

            foreach (var name in toolNames)
            {
                var tool = _toolRegistry.Get(name);
                if (tool is null)
                    return (definitions, tools, $"Tool '{name}' not found in tool registry.");

                tools[name] = tool;
                definitions.Add(tool.Definition);
            }
        }

        // Auto-inject transfer_to_agent tool if agent has handoff targets
        var agent = LlmClientResolver.TryGetAgent(context, _agentRegistry);
        if (agent is not null && agent.HandoffTargets.Count > 0
                              && agent.HandoffPolicy != HandoffPolicy.Disabled)
        {
            var transferTool = new TransferToAgentTool(agent.HandoffTargets);
            tools[transferTool.Name] = transferTool;
            definitions.Add(transferTool.Definition);
        }

        // Auto-inject delegate_to_agent tool if agent has supervisor workers
        if (agent is not null && agent.SupervisorWorkers.Count > 0
                              && agent.DelegationPolicy != DelegationPolicy.Disabled)
        {
            var delegateTool = new DelegateToAgentTool(
                agent.SupervisorWorkers,
                _providerRegistry, _agentRegistry, _toolRegistry,
                _promptRenderer, _promptRegistry, _eventSink);
            tools[delegateTool.Name] = delegateTool;
            definitions.Add(delegateTool.Definition);
            // Auto-inject memory tools if memory store is configured and enabled
            if (_memoryStore is not null && (_memoryOptions?.AutoInjectAgentTools ?? false))
            {
                var defaultNs = _memoryOptions?.DefaultNamespace ?? MemoryNamespace.Global;

                if (!tools.ContainsKey("recall_memory"))
                {
                    var recallTool = new RecallMemoryTool(_memoryStore, defaultNs);
                    tools[recallTool.Name] = recallTool;
                    definitions.Add(recallTool.Definition);
                }

                if (!tools.ContainsKey("store_memory"))
                {
                    var storeTool = new StoreMemoryTool(_memoryStore, defaultNs);
                    tools[storeTool.Name] = storeTool;
                    definitions.Add(storeTool.Definition);
                }
            }

            return (definitions, tools, null);
        }

        return (definitions, tools, null);
    }

    private List<LlmMessage> BuildInitialMessages(StepContext context)
    {
        // If pre-built messages are provided, use them as seed (multi-turn re-entry)
        if (context.Inputs.TryGetValue("messages", out var msgObj) && msgObj is IEnumerable<LlmMessage> seedMessages)
            return seedMessages.ToList();

        var messages = new List<LlmMessage>();

        // User prompt (required for first entry)
        var userPrompt = LlmClientResolver.GetStringInput(context, "userPrompt");
        if (string.IsNullOrEmpty(userPrompt))
        {
            // Try userPromptRef via prompt registry
            var userPromptRef = LlmClientResolver.GetStringInput(context, "userPromptRef");
            if (!string.IsNullOrEmpty(userPromptRef) && _promptRegistry is not null)
            {
                var template = _promptRegistry.GetPrompt(userPromptRef);
                if (template is not null)
                    userPrompt = LlmClientResolver.RenderTemplate(template.Content, context, _promptRenderer);
            }
        }
        else
        {
            userPrompt = LlmClientResolver.RenderTemplate(userPrompt, context, _promptRenderer);
        }

        if (!string.IsNullOrEmpty(userPrompt))
            messages.Add(LlmMessage.FromText(LlmRole.User, userPrompt));

        return messages;
    }

    private async Task<List<(ToolCall Call, ToolResult Result)>> ExecuteToolCallsAsync(
        List<ToolCall> toolCalls,
        Dictionary<string, ITool> tools,
        StepContext context,
        int iteration)
    {
        var tasks = toolCalls.Select(async tc =>
        {
            var sw = Stopwatch.StartNew();
            ToolResult result;

            try
            {
                if (!tools.TryGetValue(tc.Name, out var tool))
                {
                    result = ToolResult.Fail($"Tool '{tc.Name}' is not available.");
                }
                else
                {
                    result = await tool.ExecuteAsync(tc.Arguments, context.State, context.CancellationToken);
                }
            }
            catch (Contracts.Interrupts.InterruptException)
            {
                // Let interrupt exceptions propagate — the runner will checkpoint
                throw;
            }
            catch (Exception ex)
            {
                result = ToolResult.Fail($"Tool execution error: {ex.Message}");
            }

            sw.Stop();

            // Emit tool call event
            await EmitEventAsync(new AgentToolCallEvent
            {
                RunId = context.RunId,
                WorkflowId = context.WorkflowId,
                NodeId = context.NodeId,
                EventType = "agent.tool_call",
                Iteration = iteration,
                ToolName = tc.Name,
                ToolCallId = tc.Id,
                Arguments = tc.Arguments,
                ToolSuccess = result.Success,
                ToolResultContent = result.Content,
                ToolError = result.Error,
                Duration = sw.Elapsed
            }, context);

            return (tc, result);
        });

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private static async Task<LlmResponse> ExecuteStreamingAsync(
        ILlmStreamClient streamClient,
        LlmRequest request,
        Func<string, CancellationToken, Task> onToken,
        CancellationToken cancellationToken)
    {
        var accumulated = new System.Text.StringBuilder();

        await foreach (var chunk in streamClient.StreamAsync(request, cancellationToken))
        {
            accumulated.Append(chunk);
            await onToken(chunk, cancellationToken);
        }

        return new LlmResponse
        {
            Content = accumulated.ToString(),
            Success = true,
            Model = request.Model
        };
    }

    private async Task EmitEventAsync(WorkflowEvent evt, StepContext context)
    {
        if (_eventSink is not null)
        {
            evt = evt with
            {
                TenantId = evt.TenantId ?? context.RunContext.TenantId,
                UserId = evt.UserId ?? context.RunContext.UserId
            };
            await _eventSink.PublishAsync(evt, context.CancellationToken);
        }
    }

    // ── Multi-agent handoff interception ──

    private StepResult? HandleHandoffRequest(
        StepContext context,
        AgentDefinition? agent,
        ToolCall transferCall,
        List<LlmMessage> messages,
        int iteration,
        int totalInputTokens,
        int totalOutputTokens,
        string model)
    {
        if (agent is null)
            return null; // No agent definition, can't validate handoff

        var targetAgent = transferCall.Arguments.TryGetValue("target_agent", out var ta) ? ta?.ToString() : null;
        var intent = transferCall.Arguments.TryGetValue("intent", out var i) ? i?.ToString() ?? "" : "";
        var contextData = transferCall.Arguments.TryGetValue("context", out var cd) ? cd?.ToString() : null;
        var constraintsRaw = transferCall.Arguments.TryGetValue("constraints", out var cr) ? cr?.ToString() : null;

        if (string.IsNullOrEmpty(targetAgent))
        {
            // Add error as tool result so the LLM can retry
            messages.Add(new LlmMessage
            {
                Role = LlmRole.Assistant,
                Content = "",
                ToolCalls = [transferCall]
            });
            messages.Add(LlmMessage.ToolResult(transferCall.Id,
                "Error: target_agent is required for transfer_to_agent."));
            return null; // Continue loop
        }

        // Get or create execution context
        var execCtx = AgentExecutionContextHelper.GetOrCreate(
            context, agent, context.WorkflowDefinition);

        // Validate against guard rails
        var validationError = AgentExecutionContextHelper.ValidateHandoff(
            execCtx, agent, targetAgent, context.WorkflowDefinition);

        if (validationError is not null)
        {
            // Emit blocked event
            // Emit blocked event
            EmitEventAsync(new AgentHandoffBlockedEvent
            {
                RunId = context.RunId,
                WorkflowId = context.WorkflowId,
                NodeId = context.NodeId,
                EventType = nameof(AgentHandoffBlockedEvent),
                FromAgent = agent.Id,
                ToAgent = targetAgent,
                Reason = validationError
            }, context).GetAwaiter().GetResult();

            // Add error as tool result so the LLM adapts
            messages.Add(new LlmMessage
            {
                Role = LlmRole.Assistant,
                Content = "",
                ToolCalls = [transferCall]
            });
            messages.Add(LlmMessage.ToolResult(transferCall.Id,
                $"Handoff blocked: {validationError} You must complete the task yourself."));
            return null; // Continue loop — the LLM must handle it
        }

        // Build transferred messages based on conversation scope
        var transferredMessages = BuildTransferredMessages(messages, agent);

        // Build constraints list
        var constraints = new List<string>();
        if (!string.IsNullOrEmpty(constraintsRaw))
            constraints.AddRange(constraintsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        // Build handoff payload
        var payload = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(contextData))
            payload["context"] = contextData;

        // Update execution context
        execCtx.ChainDepth++;
        execCtx.VisitedAgents.Add(agent.Id);
        execCtx.TotalTokensConsumed += totalInputTokens + totalOutputTokens;
        execCtx.HandoffHistory.Add(new AgentHandoffRecord
        {
            FromAgent = agent.Id,
            ToAgent = targetAgent,
            Intent = intent,
            Timestamp = DateTimeOffset.UtcNow,
            ChainDepth = execCtx.ChainDepth,
            TokensConsumedAtHandoff = execCtx.TotalTokensConsumed
        });
        execCtx.ParentAgentId = agent.Id;

        var handoff = new AgentHandoff
        {
            FromAgent = agent.Id,
            ToAgent = targetAgent,
            Intent = intent,
            Payload = payload,
            Constraints = constraints,
            TransferredMessages = transferredMessages,
            ConversationScope = agent.ConversationScope
        };

        // Emit handoff event
        // Emit handoff event
        EmitEventAsync(new AgentHandoffEvent
        {
            RunId = context.RunId,
            WorkflowId = context.WorkflowId,
            NodeId = context.NodeId,
            EventType = nameof(AgentHandoffEvent),
            FromAgent = agent.Id,
            ToAgent = targetAgent,
            Intent = intent,
            ChainDepth = execCtx.ChainDepth,
            ConversationScope = agent.ConversationScope,
            TokensBudgetPassed = execCtx.GlobalBudgetRemaining
        }, context).GetAwaiter().GetResult();

        var outputs = BuildOutputs(
            messages.LastOrDefault(m => m.Role == LlmRole.Assistant)?.Content ?? string.Empty,
            messages, iteration, totalInputTokens, totalOutputTokens, model, "handoff");
        outputs[AgentExecutionContextHelper.ContextKey] = execCtx;

        return StepResult.HandoffTo(handoff, outputs);
    }

    private static List<LlmMessage>? BuildTransferredMessages(
        List<LlmMessage> messages,
        AgentDefinition agent)
    {
        return agent.ConversationScope switch
        {
            ConversationScope.Full => messages.ToList(),
            ConversationScope.LastN => messages.TakeLast(agent.MaxContextMessages).ToList(),
            ConversationScope.Summary => null, // TODO: Generate summary via LLM call
            ConversationScope.Handoff => null, // No messages transferred
            _ => null
        };
    }

    // ── Escalation handling ──

    private async Task<StepResult?> HandleEscalationAsync(
        StepContext context,
        AgentDefinition agent,
        string reason,
        string details,
        List<LlmMessage> messages,
        int iterations,
        int totalInputTokens,
        int totalOutputTokens,
        string model)
    {
        if (agent.EscalationTarget is null)
            return null;

        await EmitEventAsync(new AgentEscalationEvent
        {
            RunId = context.RunId,
            WorkflowId = context.WorkflowId,
            NodeId = context.NodeId,
            EventType = nameof(AgentEscalationEvent),
            FailedAgent = agent.Id,
            EscalationTarget = agent.EscalationTarget,
            Reason = reason,
            FailureDetails = details
        }, context);

        // "human" is a reserved escalation target — triggers an interrupt
        if (agent.EscalationTarget.Equals("human", StringComparison.OrdinalIgnoreCase))
        {
            var interruptRequest = new InterruptRequest
            {
                RunId = context.RunId,
                WorkflowId = context.WorkflowId,
                NodeId = context.NodeId,
                Reason = $"Agent '{agent.Id}' escalated: {reason}",
                Title = $"Escalation from '{agent.Id}'"
            };

            try
            {
                await context.InterruptAsync(interruptRequest);
            }
            catch (InterruptException)
            {
                throw; // Let the runner checkpoint and suspend
            }
        }

        // For agent escalation targets, produce a handoff
        var execCtx = AgentExecutionContextHelper.GetOrCreate(
            context, agent, context.WorkflowDefinition);
        execCtx.ChainDepth++;
        execCtx.VisitedAgents.Add(agent.Id);
        execCtx.ParentAgentId = agent.Id;

        var handoff = new AgentHandoff
        {
            FromAgent = agent.Id,
            ToAgent = agent.EscalationTarget,
            Intent = $"escalation:{reason}",
            Payload = new Dictionary<string, object?>
            {
                ["escalationReason"] = reason,
                ["failureDetails"] = details
            },
            Constraints = [$"This is an escalation from '{agent.Id}' that {reason}."],
            TransferredMessages = BuildTransferredMessages(messages, agent),
            ConversationScope = agent.ConversationScope
        };

        var outputs = BuildOutputs(
            messages.LastOrDefault(m => m.Role == LlmRole.Assistant)?.Content ?? string.Empty,
            messages, iterations, totalInputTokens, totalOutputTokens, model, "escalation");
        outputs[AgentExecutionContextHelper.ContextKey] = execCtx;

        return StepResult.HandoffTo(handoff, outputs);
    }

    private static Dictionary<string, object?> BuildOutputs(
        string response,
        List<LlmMessage> messages,
        int iterations,
        int totalInputTokens,
        int totalOutputTokens,
        string model,
        string stopReason) => new()
        {
            ["response"] = response,
            ["messages"] = messages,
            ["iterations"] = iterations,
            ["totalInputTokens"] = totalInputTokens,
            ["totalOutputTokens"] = totalOutputTokens,
            ["model"] = model,
            ["stopReason"] = stopReason
        };
}