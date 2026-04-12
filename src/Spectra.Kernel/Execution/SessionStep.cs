using System.Diagnostics;
using System.Text.Json;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Prompts;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Tools;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Prompts;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Built-in step that manages a multi-turn conversational session.
/// Unlike <see cref="AgentStep"/> which runs a single agentic loop to completion,
/// SessionStep processes one user turn at a time and suspends between turns,
/// persisting conversation history via checkpointing.
///
/// Lifecycle:
///   1. First entry (no history): optionally generate greeting, then await input.
///   2. User message arrives (via resume): run agent loop for one turn, respond, await input.
///   3. Repeat until an exit policy is satisfied, then return Success.
/// </summary>
public class SessionStep : IStep
{
    private readonly IProviderRegistry _providerRegistry;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IToolRegistry _toolRegistry;
    private readonly PromptRenderer _promptRenderer;
    private readonly IPromptRegistry? _promptRegistry;
    private readonly IEventSink? _eventSink;

    public string StepType => "session";

    public SessionStep(
        IProviderRegistry providerRegistry,
        IAgentRegistry agentRegistry,
        IToolRegistry toolRegistry,
        PromptRenderer promptRenderer,
        IPromptRegistry? promptRegistry = null,
        IEventSink? eventSink = null)
    {
        _providerRegistry = providerRegistry;
        _agentRegistry = agentRegistry;
        _toolRegistry = toolRegistry;
        _promptRenderer = promptRenderer;
        _promptRegistry = promptRegistry;
        _eventSink = eventSink;
    }

    public async Task<StepResult> ExecuteAsync(StepContext context)
    {
        try
        {
            // 1. Resolve LLM client
            var (client, clientError) = LlmClientResolver.ResolveClient(
                context, _agentRegistry, _providerRegistry);

            if (client is null)
                return StepResult.Fail(clientError ?? "Failed to resolve LLM client.");

            // 2. Resolve agent and system prompt
            var agent = LlmClientResolver.TryGetAgent(context, _agentRegistry);
            var systemPrompt = LlmClientResolver.ResolveSystemPrompt(
                context, agent, _promptRegistry, _promptRenderer);

            // 3. Load session state from workflow context
            var sessionKey = $"__session:{context.NodeId}";
            var history = LoadHistory(context, sessionKey);
            var turnCount = GetSessionInt(context, sessionKey, "turns");
            var totalInputTokens = GetSessionInt(context, sessionKey, "inputTokens");
            var totalOutputTokens = GetSessionInt(context, sessionKey, "outputTokens");
            var sessionStartedAt = GetSessionDateTimeOffset(context, sessionKey, "startedAt")
                                   ?? DateTimeOffset.UtcNow;

            // 4. Read session configuration
            var exitPolicies = (SessionExitPolicy)LlmClientResolver.GetIntInput(
                context, "__exitPolicies", (int)(SessionExitPolicy.MaxTurns | SessionExitPolicy.LlmDecides));
            var maxTurns = LlmClientResolver.GetIntInput(context, "__maxTurns", 50);
            var tokenBudget = LlmClientResolver.GetIntInput(context, "__tokenBudget", 0);
            var timeoutSeconds = LlmClientResolver.GetDoubleInput(context, "__timeout", 0);
            var exitCondition = LlmClientResolver.GetStringInput(context, "__exitCondition");
            var historyStrategy = LlmClientResolver.GetStringInput(context, "__historyStrategy") ?? "Full";
            var maxHistoryMessages = LlmClientResolver.GetIntInput(context, "__maxHistoryMessages", 100);
            var greetingPrompt = LlmClientResolver.GetStringInput(context, "__greetingPrompt");

            // 5. Read the pending user message
            var userMessage = LlmClientResolver.GetStringInput(context, "userMessage");

            // 6. First entry — generate greeting if configured
            if (history.Count == 0 && string.IsNullOrEmpty(userMessage))
            {
                if (!string.IsNullOrEmpty(greetingPrompt))
                {
                    var rendered = LlmClientResolver.RenderTemplate(greetingPrompt, context, _promptRenderer);
                    var greetingRequest = new LlmRequest
                    {
                        Model = agent?.Model ?? "unknown",
                        Messages = [LlmMessage.FromText(LlmRole.User, rendered)],
                        SystemPrompt = systemPrompt,
                        Temperature = agent?.Temperature ?? 0.7,
                        MaxTokens = agent?.MaxTokens ?? 2048,
                        SkipCache = true
                    };

                    var greetingResponse = await client.CompleteAsync(
                        greetingRequest, context.CancellationToken);

                    if (greetingResponse.Success)
                    {
                        history.Add(LlmMessage.FromText(LlmRole.Assistant, greetingResponse.Content));
                        totalInputTokens += greetingResponse.InputTokens ?? 0;
                        totalOutputTokens += greetingResponse.OutputTokens ?? 0;
                    }
                }

                // Save state and await first user message
                SaveSessionState(context, sessionKey, history, 0,
                    totalInputTokens, totalOutputTokens, sessionStartedAt);

                var greetingOutputs = BuildOutputs(
                    history.LastOrDefault(m => m.Role == LlmRole.Assistant)?.Content,
                    history, 0, totalInputTokens, totalOutputTokens, null);

                await EmitEventAsync(new SessionAwaitingInputEvent
                {
                    RunId = context.RunId,
                    WorkflowId = context.WorkflowId,
                    NodeId = context.NodeId,
                    EventType = nameof(SessionAwaitingInputEvent),
                    TurnsCompleted = 0,
                    TotalInputTokens = totalInputTokens,
                    TotalOutputTokens = totalOutputTokens
                }, context);

                return StepResult.AwaitingInput(greetingOutputs);
            }

            // 7. No user message on resume — this shouldn't happen, but handle gracefully
            if (string.IsNullOrEmpty(userMessage))
            {
                SaveSessionState(context, sessionKey, history, turnCount,
                    totalInputTokens, totalOutputTokens, sessionStartedAt);
                return StepResult.AwaitingInput(BuildOutputs(
                    null, history, turnCount, totalInputTokens, totalOutputTokens, null));
            }

            // 8. Check pre-turn exit conditions
            // UserCommand exit check
            if (exitPolicies.HasFlag(SessionExitPolicy.UserCommand))
            {
                var exitCommands = GetExitCommands(context);
                if (exitCommands.Any(cmd =>
                    userMessage.Trim().Equals(cmd, StringComparison.OrdinalIgnoreCase)))
                {
                    SaveSessionState(context, sessionKey, history, turnCount,
                        totalInputTokens, totalOutputTokens, sessionStartedAt);

                    await EmitSessionCompleted(context, turnCount, "user_command",
                        totalInputTokens, totalOutputTokens);

                    return StepResult.Success(BuildOutputs(
                        null, history, turnCount, totalInputTokens, totalOutputTokens, "user_command"));
                }
            }

            // Timeout exit check
            if (exitPolicies.HasFlag(SessionExitPolicy.Timeout) && timeoutSeconds > 0)
            {
                var elapsed = DateTimeOffset.UtcNow - sessionStartedAt;
                if (elapsed.TotalSeconds >= timeoutSeconds)
                {
                    SaveSessionState(context, sessionKey, history, turnCount,
                        totalInputTokens, totalOutputTokens, sessionStartedAt);

                    await EmitSessionCompleted(context, turnCount, "timeout",
                        totalInputTokens, totalOutputTokens);

                    return StepResult.Success(BuildOutputs(
                        null, history, turnCount, totalInputTokens, totalOutputTokens, "timeout"));
                }
            }

            // 9. Append user message to history
            history.Add(LlmMessage.FromText(LlmRole.User, userMessage));

            // 10. Resolve tools
            var (toolDefinitions, tools) = ResolveTools(context, agent, exitPolicies);

            // 11. Run agent loop for this turn (tool calls until final response)
            var turnStopwatch = Stopwatch.StartNew();
            var model = agent?.Model
                        ?? LlmClientResolver.GetStringInput(context, "model")
                        ?? "unknown";
            var temperature = LlmClientResolver.GetDoubleInput(
                context, "temperature", agent?.Temperature ?? 0.7);
            var maxTokens = LlmClientResolver.GetIntInput(
                context, "maxTokens", agent?.MaxTokens ?? 2048);

            var maxToolIterations = LlmClientResolver.GetIntInput(context, "maxIterations", 10);
            var toolIteration = 0;
            var turnInputTokens = 0;
            var turnOutputTokens = 0;
            var turnToolCalls = 0;
            string? assistantResponse = null;
            string? exitReason = null;

            while (toolIteration < maxToolIterations)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                toolIteration++;

                var request = new LlmRequest
                {
                    Model = model,
                    Messages = history.ToList(),
                    SystemPrompt = systemPrompt,
                    Temperature = temperature,
                    MaxTokens = maxTokens,
                    Tools = toolDefinitions.Count > 0 ? toolDefinitions : null,
                    SkipCache = true
                };

                LlmResponse response;

                if (context.IsStreaming && client is ILlmStreamClient streamClient
                    && !response_has_tools(toolDefinitions))
                {
                    response = await ExecuteStreamingAsync(
                        streamClient, request, context.OnToken!, context.CancellationToken);
                }
                else
                {
                    response = await client.CompleteAsync(request, context.CancellationToken);
                }

                if (!response.Success)
                    return StepResult.Fail(
                        response.ErrorMessage ?? $"LLM request failed at turn {turnCount + 1}, iteration {toolIteration}.");

                turnInputTokens += response.InputTokens ?? 0;
                turnOutputTokens += response.OutputTokens ?? 0;

                if (response.HasToolCalls)
                {
                    // Check for end_session tool call (LlmDecides exit)
                    var endSessionCall = response.ToolCalls!.FirstOrDefault(
                        tc => tc.Name.Equals("end_session", StringComparison.OrdinalIgnoreCase));

                    if (endSessionCall is not null && exitPolicies.HasFlag(SessionExitPolicy.LlmDecides))
                    {
                        // Extract final message from the LLM content or tool args
                        assistantResponse = response.Content;
                        if (string.IsNullOrEmpty(assistantResponse))
                        {
                            assistantResponse = endSessionCall.Arguments.TryGetValue("message", out var msg)
                                ? msg?.ToString() ?? "Session ended."
                                : "Session ended.";
                        }

                        exitReason = "llm_decided";
                        break;
                    }

                    // Append assistant message with tool calls
                    history.Add(new LlmMessage
                    {
                        Role = LlmRole.Assistant,
                        Content = response.Content,
                        ToolCalls = response.ToolCalls
                    });

                    // Execute tool calls (excluding end_session)
                    var callsToExecute = response.ToolCalls!
                        .Where(tc => !tc.Name.Equals("end_session", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    turnToolCalls += callsToExecute.Count;

                    foreach (var tc in callsToExecute)
                    {
                        ToolResult toolResult;
                        try
                        {
                            if (!tools.TryGetValue(tc.Name, out var tool))
                            {
                                toolResult = ToolResult.Fail($"Tool '{tc.Name}' is not available.");
                            }
                            else
                            {
                                toolResult = await tool.ExecuteAsync(
                                    tc.Arguments, context.State, context.CancellationToken);
                            }
                        }
                        catch (Exception ex)
                        {
                            toolResult = ToolResult.Fail($"Tool execution error: {ex.Message}");
                        }

                        var resultContent = toolResult.Success
                            ? toolResult.Content ?? string.Empty
                            : $"Error: {toolResult.Error ?? "Tool execution failed."}";

                        history.Add(LlmMessage.ToolResult(tc.Id, resultContent));
                    }
                }
                else
                {
                    // Final response for this turn — no tool calls
                    assistantResponse = response.Content;
                    history.Add(LlmMessage.FromText(LlmRole.Assistant, assistantResponse));
                    break;
                }
            }

            // If we exited the loop without a response (hit max tool iterations)
            if (assistantResponse is null)
            {
                var lastAssistant = history.LastOrDefault(m => m.Role == LlmRole.Assistant);
                assistantResponse = lastAssistant?.Content ?? string.Empty;
            }

            turnStopwatch.Stop();
            turnCount++;
            totalInputTokens += turnInputTokens;
            totalOutputTokens += turnOutputTokens;

            // 12. Apply history windowing
            history = ApplyHistoryStrategy(history, historyStrategy, maxHistoryMessages);

            // 13. Emit turn completed event
            await EmitEventAsync(new SessionTurnCompletedEvent
            {
                RunId = context.RunId,
                WorkflowId = context.WorkflowId,
                NodeId = context.NodeId,
                EventType = nameof(SessionTurnCompletedEvent),
                TurnNumber = turnCount,
                AssistantResponse = assistantResponse,
                InputTokens = turnInputTokens,
                OutputTokens = turnOutputTokens,
                ToolCallCount = turnToolCalls,
                Duration = turnStopwatch.Elapsed
            }, context);

            // 14. Check post-turn exit conditions
            if (exitReason is null)
                exitReason = EvaluateExitConditions(
                    exitPolicies, turnCount, maxTurns,
                    totalInputTokens + totalOutputTokens, tokenBudget,
                    sessionStartedAt, timeoutSeconds,
                    exitCondition, context);

            // 15. Save session state
            SaveSessionState(context, sessionKey, history, turnCount,
                totalInputTokens, totalOutputTokens, sessionStartedAt);

            // 16. Exit or await next turn
            if (exitReason is not null)
            {
                await EmitSessionCompleted(context, turnCount, exitReason,
                    totalInputTokens, totalOutputTokens);

                return StepResult.Success(BuildOutputs(
                    assistantResponse, history, turnCount,
                    totalInputTokens, totalOutputTokens, exitReason));
            }

            // Await next user message
            await EmitEventAsync(new SessionAwaitingInputEvent
            {
                RunId = context.RunId,
                WorkflowId = context.WorkflowId,
                NodeId = context.NodeId,
                EventType = nameof(SessionAwaitingInputEvent),
                TurnsCompleted = turnCount,
                TotalInputTokens = totalInputTokens,
                TotalOutputTokens = totalOutputTokens
            }, context);

            return StepResult.AwaitingInput(BuildOutputs(
                assistantResponse, history, turnCount,
                totalInputTokens, totalOutputTokens, null));
        }
        catch (OperationCanceledException)
        {
            return StepResult.Fail("Session step was cancelled.");
        }
        catch (Contracts.Interrupts.InterruptException)
        {
            throw; // Let the runner checkpoint and suspend
        }
        catch (Exception ex)
        {
            return StepResult.Fail($"Session step failed: {ex.Message}", ex);
        }
    }

    // ── History persistence ──

    private static List<LlmMessage> LoadHistory(StepContext context, string sessionKey)
    {
        var historyKey = $"{sessionKey}:history";
        if (context.State.Context.TryGetValue(historyKey, out var obj) && obj is List<LlmMessage> msgs)
            return msgs.ToList();

        // Handle deserialized JSON (checkpoint round-trip)
        if (obj is JsonElement jsonElement)
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize<List<LlmMessage>>(
                    jsonElement.GetRawText());
                return deserialized ?? [];
            }
            catch
            {
                return [];
            }
        }

        return [];
    }

    private static int GetSessionInt(StepContext context, string sessionKey, string field)
    {
        var key = $"{sessionKey}:{field}";
        if (context.State.Context.TryGetValue(key, out var obj))
        {
            return obj switch
            {
                int i => i,
                long l => (int)l,
                JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
                _ => 0
            };
        }
        return 0;
    }

    private static DateTimeOffset? GetSessionDateTimeOffset(StepContext context, string sessionKey, string field)
    {
        var key = $"{sessionKey}:{field}";
        if (context.State.Context.TryGetValue(key, out var obj))
        {
            return obj switch
            {
                DateTimeOffset dto => dto,
                string s when DateTimeOffset.TryParse(s, out var parsed) => parsed,
                JsonElement je when je.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(je.GetString(), out var jeParsed) => jeParsed,
                _ => null
            };
        }
        return null;
    }

    private static void SaveSessionState(
        StepContext context,
        string sessionKey,
        List<LlmMessage> history,
        int turnCount,
        int totalInputTokens,
        int totalOutputTokens,
        DateTimeOffset sessionStartedAt)
    {
        context.State.Context[$"{sessionKey}:history"] = history;
        context.State.Context[$"{sessionKey}:turns"] = turnCount;
        context.State.Context[$"{sessionKey}:inputTokens"] = totalInputTokens;
        context.State.Context[$"{sessionKey}:outputTokens"] = totalOutputTokens;
        context.State.Context[$"{sessionKey}:startedAt"] = sessionStartedAt;
    }

    // ── History windowing ──

    private static List<LlmMessage> ApplyHistoryStrategy(
        List<LlmMessage> history, string strategy, int maxMessages)
    {
        if (strategy == "SlidingWindow" && history.Count > maxMessages)
        {
            // Keep the first message (system context) and the last N
            return history.TakeLast(maxMessages).ToList();
        }

        // Full or Summarize (not yet implemented) — return as-is
        return history;
    }

    // ── Tool resolution ──

    private (List<ToolDefinition> Definitions, Dictionary<string, ITool> Tools) ResolveTools(
        StepContext context,
        AgentDefinition? agent,
        SessionExitPolicy exitPolicies)
    {
        var definitions = new List<ToolDefinition>();
        var tools = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);

        // Resolve explicitly listed tools
        if (context.Inputs.TryGetValue("tools", out var toolsObj) && toolsObj is not null)
        {
            var toolNames = toolsObj switch
            {
                IEnumerable<string> names => names.ToList(),
                IEnumerable<object> objects => objects
                    .Select(o => o?.ToString() ?? "")
                    .Where(s => s.Length > 0)
                    .ToList(),
                string single => [single],
                _ => []
            };

            foreach (var name in toolNames)
            {
                var tool = _toolRegistry.Get(name);
                if (tool is not null)
                {
                    tools[name] = tool;
                    definitions.Add(tool.Definition);
                }
            }
        }

        // Auto-inject end_session tool when LlmDecides is active
        if (exitPolicies.HasFlag(SessionExitPolicy.LlmDecides)
            && !tools.ContainsKey("end_session"))
        {
            var endSessionDef = new ToolDefinition
            {
                Name = "end_session",
                Description = "Call this tool when the conversation is complete and no further user interaction is needed. " +
                              "Use when the user's request has been fully addressed or the user indicates they want to end the session.",
                Parameters =
                [
                    new ToolParameter
                    {
                        Name = "message",
                        Description = "Optional final message to the user before ending the session.",
                        Type = "string",
                        Required = false
                    }
                ]
            };
            definitions.Add(endSessionDef);
            // No ITool needed — end_session is intercepted before execution
        }

        return (definitions, tools);
    }

    // ── Exit condition evaluation ──

    private static string? EvaluateExitConditions(
        SessionExitPolicy policies,
        int turnCount,
        int maxTurns,
        int totalTokens,
        int tokenBudget,
        DateTimeOffset sessionStartedAt,
        double timeoutSeconds,
        string? exitCondition,
        StepContext context)
    {
        if (policies.HasFlag(SessionExitPolicy.MaxTurns) && turnCount >= maxTurns)
            return "max_turns";

        if (policies.HasFlag(SessionExitPolicy.TokenBudget) && tokenBudget > 0 && totalTokens >= tokenBudget)
            return "token_budget";

        if (policies.HasFlag(SessionExitPolicy.Timeout) && timeoutSeconds > 0)
        {
            var elapsed = DateTimeOffset.UtcNow - sessionStartedAt;
            if (elapsed.TotalSeconds >= timeoutSeconds)
                return "timeout";
        }

        // Condition evaluation would require IConditionEvaluator — defer to runner
        // For now, check if a simple state key is truthy
        if (policies.HasFlag(SessionExitPolicy.Condition) && !string.IsNullOrEmpty(exitCondition))
        {
            if (context.State.Context.TryGetValue(exitCondition, out var val) && val is true or "true")
                return "condition";
        }

        return null;
    }

    private static List<string> GetExitCommands(StepContext context)
    {
        if (context.Inputs.TryGetValue("__exitCommands", out var obj) && obj is not null)
        {
            return obj switch
            {
                IEnumerable<string> cmds => cmds.ToList(),
                IEnumerable<object> objects => objects
                    .Select(o => o?.ToString() ?? "")
                    .Where(s => s.Length > 0)
                    .ToList(),
                _ => ["/done", "/exit", "/quit"]
            };
        }
        return ["/done", "/exit", "/quit"];
    }

    // ── Streaming ──

    private static bool response_has_tools(List<ToolDefinition> toolDefs) => toolDefs.Count > 0;

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

    // ── Output building ──

    private static Dictionary<string, object?> BuildOutputs(
        string? response,
        List<LlmMessage> history,
        int turnCount,
        int totalInputTokens,
        int totalOutputTokens,
        string? exitReason) => new()
        {
            ["response"] = response,
            ["messages"] = history,
            ["turnCount"] = turnCount,
            ["totalInputTokens"] = totalInputTokens,
            ["totalOutputTokens"] = totalOutputTokens,
            ["exitReason"] = exitReason
        };

    // ── Event emission ──

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

    private async Task EmitSessionCompleted(
        StepContext context, int totalTurns, string exitReason,
        int totalInputTokens, int totalOutputTokens)
    {
        await EmitEventAsync(new SessionCompletedEvent
        {
            RunId = context.RunId,
            WorkflowId = context.WorkflowId,
            NodeId = context.NodeId,
            EventType = nameof(SessionCompletedEvent),
            TotalTurns = totalTurns,
            ExitReason = exitReason,
            TotalInputTokens = totalInputTokens,
            TotalOutputTokens = totalOutputTokens
        }, context);
    }
}