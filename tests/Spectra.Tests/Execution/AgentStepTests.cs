using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Prompts;
using Spectra.Contracts.Providers;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Tools;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Execution;
using Spectra.Kernel.Prompts;
using Xunit;

namespace Spectra.Tests.Execution;

public class AgentStepTests
{
    // ── Fakes ──────────────────────────────────────────────────────

    private class FakeLlmClient : ILlmClient
    {
        public string ProviderName => "fake";
        public string ModelId => "fake-model";
        public ModelCapabilities Capabilities => new();

        public Queue<LlmResponse> Responses { get; } = new();
        public List<LlmRequest> ReceivedRequests { get; } = [];

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            ReceivedRequests.Add(request);
            if (Responses.Count == 0)
                return Task.FromResult(new LlmResponse { Content = "done", Success = true, Model = "fake-model" });
            return Task.FromResult(Responses.Dequeue());
        }
    }

    private class FakeStreamClient : ILlmStreamClient
    {
        public string ProviderName => "fake-stream";
        public string ModelId => "fake-stream-model";
        public ModelCapabilities Capabilities => new();

        public Queue<(List<string> Chunks, LlmResponse Response)> Rounds { get; } = new();
        public List<LlmRequest> ReceivedRequests { get; } = [];

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            ReceivedRequests.Add(request);
            if (Rounds.Count == 0)
                return Task.FromResult(new LlmResponse { Content = "done", Success = true });
            var round = Rounds.Dequeue();
            return Task.FromResult(round.Response);
        }

        public async IAsyncEnumerable<string> StreamAsync(
            LlmRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ReceivedRequests.Add(request);
            if (Rounds.Count == 0) yield break;
            var round = Rounds.Dequeue();
            foreach (var chunk in round.Chunks)
            {
                await Task.Yield();
                yield return chunk;
            }
        }
    }

    private class FakeProviderRegistry : IProviderRegistry
    {
        private readonly Dictionary<string, ILlmClient> _clients = new();
        public void RegisterClient(string agentId, ILlmClient client) => _clients[agentId] = client;

        public ILlmClient? CreateClient(AgentDefinition agent)
            => _clients.TryGetValue(agent.Id, out var c) ? c : null;

        public void Register(ILlmProvider provider) { }
        public ILlmProvider? GetProvider(string name) => null;
    }

    private class FakeAgentRegistry : IAgentRegistry
    {
        private readonly Dictionary<string, AgentDefinition> _agents = new();
        public void Register(AgentDefinition agent) => _agents[agent.Id] = agent;
        public AgentDefinition? GetAgent(string agentId) =>
            _agents.TryGetValue(agentId, out var a) ? a : null;
        public IReadOnlyList<AgentDefinition> GetAll() => _agents.Values.ToList();
    }

    private class FakeToolRegistry : IToolRegistry
    {
        private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);

        public void Register(ITool tool) => _tools[tool.Name] = tool;
        public ITool? Get(string name) => _tools.TryGetValue(name, out var t) ? t : null;
        public IReadOnlyList<ITool> GetAll() => _tools.Values.ToList();
        public IReadOnlyList<ToolDefinition> GetDefinitions(IEnumerable<string>? filter = null)
        {
            if (filter is null) return _tools.Values.Select(t => t.Definition).ToList();
            return filter.Select(n => _tools.TryGetValue(n, out var t) ? t.Definition : null).Where(d => d is not null).ToList()!;
        }
    }

    private class FakeTool : ITool
    {
        public string Name { get; init; } = "test_tool";
        public ToolDefinition Definition => new()
        {
            Name = Name,
            Description = $"Fake tool: {Name}",
            Parameters = []
        };

        public Func<Dictionary<string, object?>, WorkflowState, ToolResult>? OnExecute { get; set; }
        public List<Dictionary<string, object?>> ReceivedArguments { get; } = [];

        public Task<ToolResult> ExecuteAsync(
            Dictionary<string, object?> arguments,
            WorkflowState state,
            CancellationToken ct = default)
        {
            ReceivedArguments.Add(arguments);
            var result = OnExecute?.Invoke(arguments, state) ?? ToolResult.Ok("tool result");
            return Task.FromResult(result);
        }
    }

    private class FakeEventSink : IEventSink
    {
        public List<WorkflowEvent> Events { get; } = [];

        public Task PublishAsync(WorkflowEvent evt, CancellationToken cancellationToken = default)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static AgentDefinition CreateAgent(
        string id = "agent-1",
        string provider = "openai",
        string model = "gpt-4",
        string? systemPrompt = "You are a helpful assistant.") =>
        new()
        {
            Id = id,
            Provider = provider,
            Model = model,
            SystemPrompt = systemPrompt
        };

    private static StepContext CreateContext(
        Dictionary<string, object?> inputs,
        WorkflowState? state = null,
        Func<string, CancellationToken, Task>? onToken = null) =>
        new()
        {
            RunId = "run-1",
            WorkflowId = "wf-1",
            NodeId = "node-1",
            State = state ?? new WorkflowState { WorkflowId = "wf-1" },
            CancellationToken = CancellationToken.None,
            Inputs = inputs,
            OnToken = onToken
        };

    private static LlmResponse ToolCallResponse(params (string Id, string Name, Dictionary<string, object?> Args)[] calls) =>
        new()
        {
            Content = "",
            Success = true,
            Model = "gpt-4",
            InputTokens = 10,
            OutputTokens = 5,
            ToolCalls = calls.Select(c => new ToolCall { Id = c.Id, Name = c.Name, Arguments = c.Args }).ToList()
        };

    private static LlmResponse FinalResponse(string content = "Final answer", int inputTokens = 10, int outputTokens = 20) =>
        new()
        {
            Content = content,
            Success = true,
            Model = "gpt-4",
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            StopReason = "end_turn"
        };

    private (AgentStep Step, FakeLlmClient Client, FakeToolRegistry ToolRegistry, FakeEventSink EventSink) Setup(
        AgentDefinition? agent = null,
        IEnumerable<LlmResponse>? responses = null)
    {
        agent ??= CreateAgent();
        var client = new FakeLlmClient();
        if (responses is not null)
        {
            foreach (var r in responses)
                client.Responses.Enqueue(r);
        }

        var agentReg = new FakeAgentRegistry();
        agentReg.Register(agent);

        var providerReg = new FakeProviderRegistry();
        providerReg.RegisterClient(agent.Id, client);

        var toolRegistry = new FakeToolRegistry();
        var eventSink = new FakeEventSink();

        var step = new AgentStep(
            providerReg, agentReg, toolRegistry, new PromptRenderer(),
            eventSink: eventSink);

        return (step, client, toolRegistry, eventSink);
    }

    // ── StepType ────────────────────────────────────────────────────

    [Fact]
    public void StepType_is_agent()
    {
        var (step, _, _, _) = Setup();
        Assert.Equal("agent", step.StepType);
    }

    // ── Happy Path: No Tools (single-shot) ─────────────────────────

    [Fact]
    public async Task No_tools_returns_single_completion()
    {
        var (step, client, _, _) = Setup(responses: [FinalResponse("Hello!")]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Say hello"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal("Hello!", result.Outputs["response"]);
        Assert.Equal(1, result.Outputs["iterations"]);
        Assert.Equal("end_turn", result.Outputs["stopReason"]);
        Assert.Single(client.ReceivedRequests);
    }

    // ── Happy Path: Single Tool Call Iteration ─────────────────────

    [Fact]
    public async Task Single_tool_call_iteration_then_final_response()
    {
        var (step, client, toolReg, eventSink) = Setup(responses:
        [
            ToolCallResponse(("tc-1", "search", new() { ["query"] = "test" })),
            FinalResponse("Found the answer")
        ]);

        var tool = new FakeTool { Name = "search" };
        toolReg.Register(tool);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Find something",
            ["tools"] = new[] { "search" }
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal("Found the answer", result.Outputs["response"]);
        Assert.Equal(2, result.Outputs["iterations"]);
        Assert.Equal(2, client.ReceivedRequests.Count);

        // Verify tool was called
        Assert.Single(tool.ReceivedArguments);
        Assert.Equal("test", tool.ReceivedArguments[0]["query"]);

        // Verify second request includes tool result in messages
        var secondRequest = client.ReceivedRequests[1];
        Assert.True(secondRequest.Messages.Count > 1);
        var toolResultMsg = secondRequest.Messages.FirstOrDefault(m => m.Role == LlmRole.Tool);
        Assert.NotNull(toolResultMsg);
        Assert.Equal("tc-1", toolResultMsg.ToolCallId);
        Assert.Equal("tool result", toolResultMsg.Content);
    }

    // ── Multiple Tool Calls in One Response (Parallel Execution) ───

    [Fact]
    public async Task Multiple_tool_calls_executed_in_parallel()
    {
        var (step, _, toolReg, _) = Setup(responses:
        [
            ToolCallResponse(
                ("tc-1", "search", new() { ["q"] = "a" }),
                ("tc-2", "read_file", new() { ["path"] = "test.cs" })),
            FinalResponse("Done")
        ]);

        var searchTool = new FakeTool { Name = "search" };
        var readTool = new FakeTool { Name = "read_file" };
        toolReg.Register(searchTool);
        toolReg.Register(readTool);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Analyze",
            ["tools"] = new[] { "search", "read_file" }
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Single(searchTool.ReceivedArguments);
        Assert.Single(readTool.ReceivedArguments);
    }

    // ── Multi-Iteration Loop ──────────────────────────────────────

    [Fact]
    public async Task Multi_iteration_loop_tracks_tokens()
    {
        var (step, _, toolReg, _) = Setup(responses:
        [
            ToolCallResponse(("tc-1", "search", new())),
            ToolCallResponse(("tc-2", "search", new())),
            FinalResponse("Result", inputTokens: 15, outputTokens: 25)
        ]);

        toolReg.Register(new FakeTool { Name = "search" });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Deep search",
            ["tools"] = new[] { "search" }
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal(3, result.Outputs["iterations"]);
        // 10 + 10 + 15 = 35 input tokens
        Assert.Equal(35, result.Outputs["totalInputTokens"]);
        // 5 + 5 + 25 = 35 output tokens
        Assert.Equal(35, result.Outputs["totalOutputTokens"]);
    }

    // ── Max Iterations Guard ──────────────────────────────────────

    [Fact]
    public async Task Max_iterations_stops_loop_and_returns_partial()
    {
        // All responses are tool calls — will never end naturally
        var responses = Enumerable.Range(0, 5)
            .Select(i => ToolCallResponse(($"tc-{i}", "search", new())))
            .ToList();

        var (step, _, toolReg, _) = Setup(responses: responses);
        toolReg.Register(new FakeTool { Name = "search" });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Infinite search",
            ["tools"] = new[] { "search" },
            ["maxIterations"] = 3
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal(3, result.Outputs["iterations"]);
        Assert.Equal("max_iterations", result.Outputs["stopReason"]);
    }

    // ── Tool Error Recovery ───────────────────────────────────────

    [Fact]
    public async Task Tool_error_is_sent_back_to_llm_as_error_result()
    {
        var (step, client, toolReg, _) = Setup(responses:
        [
            ToolCallResponse(("tc-1", "broken_tool", new())),
            FinalResponse("I handled the error")
        ]);

        var brokenTool = new FakeTool
        {
            Name = "broken_tool",
            OnExecute = (_, _) => throw new InvalidOperationException("Something went wrong")
        };
        toolReg.Register(brokenTool);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Try the tool",
            ["tools"] = new[] { "broken_tool" }
        });

        var result = await step.ExecuteAsync(ctx);

        // Step should succeed — the agent recovered from the tool error
        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal("I handled the error", result.Outputs["response"]);

        // Verify the error was sent to the LLM as a tool result
        var secondRequest = client.ReceivedRequests[1];
        var toolMsg = secondRequest.Messages.First(m => m.Role == LlmRole.Tool);
        Assert.Contains("Error:", toolMsg.Content);
        Assert.Contains("Something went wrong", toolMsg.Content);
    }

    [Fact]
    public async Task Tool_returning_fail_sends_error_content_to_llm()
    {
        var (step, client, toolReg, _) = Setup(responses:
        [
            ToolCallResponse(("tc-1", "failable", new())),
            FinalResponse("Recovered")
        ]);

        var tool = new FakeTool
        {
            Name = "failable",
            OnExecute = (_, _) => ToolResult.Fail("Permission denied")
        };
        toolReg.Register(tool);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Do it",
            ["tools"] = new[] { "failable" }
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        var secondRequest = client.ReceivedRequests[1];
        var toolMsg = secondRequest.Messages.First(m => m.Role == LlmRole.Tool);
        Assert.Contains("Permission denied", toolMsg.Content);
    }

    // ── Unknown Tool in Whitelist ─────────────────────────────────

    [Fact]
    public async Task Unknown_tool_in_whitelist_returns_fail()
    {
        var (step, _, _, _) = Setup();

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Use a tool",
            ["tools"] = new[] { "nonexistent_tool" }
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("nonexistent_tool", result.ErrorMessage);
    }

    // ── Missing Agent ─────────────────────────────────────────────

    [Fact]
    public async Task Missing_agent_returns_fail()
    {
        var (step, _, _, _) = Setup();

        var ctx = CreateContext(new()
        {
            ["agentId"] = "nonexistent",
            ["userPrompt"] = "Hello"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("nonexistent", result.ErrorMessage);
    }

    // ── LLM Error During Loop ─────────────────────────────────────

    [Fact]
    public async Task Llm_error_during_loop_returns_fail()
    {
        var (step, _, toolReg, _) = Setup(responses:
        [
            ToolCallResponse(("tc-1", "search", new())),
            LlmResponse.Error("Rate limit exceeded")
        ]);

        toolReg.Register(new FakeTool { Name = "search" });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Search",
            ["tools"] = new[] { "search" }
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("Rate limit", result.ErrorMessage);
    }

    // ── Event Emission ────────────────────────────────────────────

    [Fact]
    public async Task Emits_iteration_and_tool_call_and_completed_events()
    {
        var (step, _, toolReg, eventSink) = Setup(responses:
        [
            ToolCallResponse(("tc-1", "search", new() { ["q"] = "test" })),
            FinalResponse("Done")
        ]);

        toolReg.Register(new FakeTool { Name = "search" });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Go",
            ["tools"] = new[] { "search" }
        });

        await step.ExecuteAsync(ctx);

        // Should have: 1 tool call event + 1 iteration event + 1 completed event
        var toolCallEvents = eventSink.Events.OfType<AgentToolCallEvent>().ToList();
        var iterationEvents = eventSink.Events.OfType<AgentIterationEvent>().ToList();
        var completedEvents = eventSink.Events.OfType<AgentCompletedEvent>().ToList();

        Assert.Single(toolCallEvents);
        Assert.Equal("search", toolCallEvents[0].ToolName);
        Assert.Equal("tc-1", toolCallEvents[0].ToolCallId);
        Assert.True(toolCallEvents[0].ToolSuccess);

        Assert.Single(iterationEvents);
        Assert.Equal(1, iterationEvents[0].Iteration);
        Assert.Equal(1, iterationEvents[0].ToolCallCount);

        Assert.Single(completedEvents);
        Assert.Equal(2, completedEvents[0].TotalIterations);
        Assert.Equal("end_turn", completedEvents[0].StopReason);
    }

    [Fact]
    public async Task Tool_error_event_has_error_details()
    {
        var (step, _, toolReg, eventSink) = Setup(responses:
        [
            ToolCallResponse(("tc-1", "broken", new())),
            FinalResponse("ok")
        ]);

        toolReg.Register(new FakeTool
        {
            Name = "broken",
            OnExecute = (_, _) => throw new Exception("Boom")
        });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Go",
            ["tools"] = new[] { "broken" }
        });

        await step.ExecuteAsync(ctx);

        var toolEvent = eventSink.Events.OfType<AgentToolCallEvent>().Single();
        Assert.False(toolEvent.ToolSuccess);
        Assert.Contains("Boom", toolEvent.ToolError);
    }

    // ── Messages Output (conversation history) ────────────────────

    [Fact]
    public async Task Messages_output_contains_full_conversation()
    {
        var (step, _, toolReg, _) = Setup(responses:
        [
            ToolCallResponse(("tc-1", "search", new())),
            FinalResponse("The answer is 42")
        ]);

        toolReg.Register(new FakeTool { Name = "search" });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "What is the answer?",
            ["tools"] = new[] { "search" }
        });

        var result = await step.ExecuteAsync(ctx);
        var messages = result.Outputs["messages"] as List<LlmMessage>;

        Assert.NotNull(messages);
        // user → assistant (tool call) → tool result → assistant (final)
        Assert.Equal(4, messages.Count);
        Assert.Equal(LlmRole.User, messages[0].Role);
        Assert.Equal(LlmRole.Assistant, messages[1].Role);
        Assert.NotNull(messages[1].ToolCalls);
        Assert.Equal(LlmRole.Tool, messages[2].Role);
        Assert.Equal(LlmRole.Assistant, messages[3].Role);
        Assert.Equal("The answer is 42", messages[3].Content);
    }

    // ── Seed Messages (multi-turn re-entry) ───────────────────────

    [Fact]
    public async Task Seed_messages_are_prepended_to_conversation()
    {
        var (step, client, _, _) = Setup(responses: [FinalResponse("Continued")]);

        var seedMessages = new List<LlmMessage>
        {
            LlmMessage.FromText(LlmRole.User, "First message"),
            LlmMessage.FromText(LlmRole.Assistant, "First response")
        };

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["messages"] = seedMessages
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        // The LLM request should contain the seed messages
        var request = client.ReceivedRequests[0];
        Assert.Equal(2, request.Messages.Count);
        Assert.Equal("First message", request.Messages[0].Content);
        Assert.Equal("First response", request.Messages[1].Content);
    }

    // ── System Prompt ─────────────────────────────────────────────

    [Fact]
    public async Task System_prompt_from_agent_is_forwarded()
    {
        var (step, client, _, _) = Setup(responses: [FinalResponse()]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Test"
        });

        await step.ExecuteAsync(ctx);

        Assert.Equal("You are a helpful assistant.", client.ReceivedRequests[0].SystemPrompt);
    }

    [Fact]
    public async Task System_prompt_input_overrides_agent()
    {
        var (step, client, _, _) = Setup(responses: [FinalResponse()]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["systemPrompt"] = "You are a pirate.",
            ["userPrompt"] = "Ahoy"
        });

        await step.ExecuteAsync(ctx);

        Assert.Equal("You are a pirate.", client.ReceivedRequests[0].SystemPrompt);
    }

    // ── Tool Definitions Sent to LLM ──────────────────────────────

    [Fact]
    public async Task Tool_definitions_are_sent_in_llm_request()
    {
        var (step, client, toolReg, _) = Setup(responses: [FinalResponse()]);

        toolReg.Register(new FakeTool { Name = "search" });
        toolReg.Register(new FakeTool { Name = "edit" });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Go",
            ["tools"] = new[] { "search", "edit" }
        });

        await step.ExecuteAsync(ctx);

        var request = client.ReceivedRequests[0];
        Assert.NotNull(request.Tools);
        Assert.Equal(2, request.Tools!.Count);
        Assert.Contains(request.Tools, t => t.Name == "search");
        Assert.Contains(request.Tools, t => t.Name == "edit");
    }

    [Fact]
    public async Task No_tools_means_no_tool_definitions_in_request()
    {
        var (step, client, _, _) = Setup(responses: [FinalResponse()]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Just answer"
        });

        await step.ExecuteAsync(ctx);

        Assert.Null(client.ReceivedRequests[0].Tools);
    }

    // ── Structured Output on Final Response ───────────────────────

    [Fact]
    public async Task Output_schema_validates_final_json_response()
    {
        var (step, _, toolReg, _) = Setup(responses:
        [
            ToolCallResponse(("tc-1", "search", new())),
            new LlmResponse
            {
                Content = """{"answer": 42}""",
                Success = true,
                Model = "gpt-4",
                InputTokens = 10,
                OutputTokens = 10
            }
        ]);

        toolReg.Register(new FakeTool { Name = "search" });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Find and return structured",
            ["tools"] = new[] { "search" },
            ["outputSchema"] = """{"type":"object"}"""
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.True(result.Outputs.ContainsKey("parsedResponse"));
    }

    [Fact]
    public async Task Output_schema_invalid_json_returns_fail()
    {
        var (step, _, _, _) = Setup(responses:
        [
            new LlmResponse
            {
                Content = "This is not JSON",
                Success = true,
                Model = "gpt-4",
                InputTokens = 5,
                OutputTokens = 5
            }
        ]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Return JSON",
            ["outputSchema"] = """{"type":"object"}"""
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("not valid JSON", result.ErrorMessage);
        // Should still include partial outputs
        Assert.Equal("This is not JSON", result.Outputs["response"]);
    }

    // ── Template Rendering ────────────────────────────────────────

    [Fact]
    public async Task User_prompt_renders_template_variables()
    {
        var (step, client, _, _) = Setup(responses: [FinalResponse()]);

        var state = new WorkflowState { WorkflowId = "wf-1" };
        state.Context["fileName"] = "Program.cs";

        var ctx = CreateContext(
            new()
            {
                ["agentId"] = "agent-1",
                ["userPrompt"] = "Review the file {{fileName}}"
            },
            state: state);

        await step.ExecuteAsync(ctx);

        var userMsg = client.ReceivedRequests[0].Messages[0];
        Assert.Equal("Review the file Program.cs", userMsg.Content);
    }

    // ── Streaming ─────────────────────────────────────────────────

    [Fact]
    public async Task Streaming_invokes_on_token_when_no_tools()
    {
        var agent = CreateAgent();
        var streamClient = new FakeStreamClient();
        streamClient.Rounds.Enqueue((
            ["The ", "answer"],
            FinalResponse("The answer")
        ));

        var agentReg = new FakeAgentRegistry();
        agentReg.Register(agent);
        var providerReg = new FakeProviderRegistry();
        providerReg.RegisterClient(agent.Id, streamClient);

        var step = new AgentStep(providerReg, agentReg, new FakeToolRegistry(), new PromptRenderer());

        var tokens = new List<string>();
        var ctx = CreateContext(
            new()
            {
                ["agentId"] = "agent-1",
                ["userPrompt"] = "Stream test"
            },
            onToken: (token, _) => { tokens.Add(token); return Task.CompletedTask; });

        await step.ExecuteAsync(ctx);

        Assert.Equal(["The ", "answer"], tokens);
    }

    // ── Cancellation ──────────────────────────────────────────────

    [Fact]
    public async Task Cancelled_token_returns_fail()
    {
        var (step, _, _, _) = Setup(responses: [FinalResponse()]);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var ctx = new StepContext
        {
            RunId = "run-1",
            WorkflowId = "wf-1",
            NodeId = "node-1",
            State = new WorkflowState { WorkflowId = "wf-1" },
            CancellationToken = cts.Token,
            Inputs = new()
            {
                ["agentId"] = "agent-1",
                ["userPrompt"] = "Hello"
            }
        };

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("cancelled", result.ErrorMessage);
    }

    // ── SkipCache Is Always True ──────────────────────────────────

    [Fact]
    public async Task Requests_always_skip_cache()
    {
        var (step, client, _, _) = Setup(responses: [FinalResponse()]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Test"
        });

        await step.ExecuteAsync(ctx);

        Assert.True(client.ReceivedRequests[0].SkipCache);
    }

    // ── Tool Receives Workflow State ──────────────────────────────

    [Fact]
    public async Task Tool_receives_workflow_state()
    {
        WorkflowState? receivedState = null;

        var (step, _, toolReg, _) = Setup(responses:
        [
            ToolCallResponse(("tc-1", "stateful", new())),
            FinalResponse()
        ]);

        toolReg.Register(new FakeTool
        {
            Name = "stateful",
            OnExecute = (_, state) =>
            {
                receivedState = state;
                return ToolResult.Ok("ok");
            }
        });

        var workflowState = new WorkflowState { WorkflowId = "wf-1" };
        workflowState.Context["key"] = "value";

        var ctx = CreateContext(
            new()
            {
                ["agentId"] = "agent-1",
                ["userPrompt"] = "Go",
                ["tools"] = new[] { "stateful" }
            },
            state: workflowState);

        await step.ExecuteAsync(ctx);

        Assert.NotNull(receivedState);
        Assert.Equal("value", receivedState!.Context["key"]);
    }
}