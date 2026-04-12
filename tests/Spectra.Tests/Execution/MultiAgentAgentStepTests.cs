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

public class MultiAgentAgentStepTests
{
    // ── Fakes (reused from AgentStepTests pattern) ──

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
            return filter.Select(n => _tools.TryGetValue(n, out var t) ? t.Definition : null)
                .Where(d => d is not null).ToList()!;
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

        public Task<ToolResult> ExecuteAsync(
            Dictionary<string, object?> arguments,
            WorkflowState state,
            CancellationToken ct = default)
        {
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

    // ── Helpers ──

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

    private static StepContext CreateContext(
        Dictionary<string, object?> inputs,
        WorkflowState? state = null,
        WorkflowDefinition? workflowDef = null) =>
        new()
        {
            RunId = "run-1",
            WorkflowId = "wf-1",
            NodeId = "node-1",
            State = state ?? new WorkflowState { WorkflowId = "wf-1" },
            CancellationToken = CancellationToken.None,
            Inputs = inputs,
            WorkflowDefinition = workflowDef
        };

    private (AgentStep Step, FakeLlmClient Client, FakeAgentRegistry AgentReg, FakeToolRegistry ToolReg, FakeEventSink EventSink)
        Setup(AgentDefinition agent, IEnumerable<LlmResponse>? responses = null)
    {
        var client = new FakeLlmClient();
        if (responses is not null)
            foreach (var r in responses)
                client.Responses.Enqueue(r);

        var agentReg = new FakeAgentRegistry();
        agentReg.Register(agent);

        var providerReg = new FakeProviderRegistry();
        providerReg.RegisterClient(agent.Id, client);

        var toolReg = new FakeToolRegistry();
        var eventSink = new FakeEventSink();

        var step = new AgentStep(
            providerReg, agentReg, toolReg, new PromptRenderer(),
            eventSink: eventSink);

        return (step, client, agentReg, toolReg, eventSink);
    }

    // ── Auto-inject transfer_to_agent tool ──

    [Fact]
    public async Task Agent_with_handoff_targets_gets_transfer_tool_injected()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-1",
            Provider = "openai",
            Model = "gpt-4",
            HandoffTargets = ["agent-b", "agent-c"],
            HandoffPolicy = HandoffPolicy.Allowed
        };

        var (step, client, _, _, _) = Setup(agent, [FinalResponse()]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Do something"
        });

        await step.ExecuteAsync(ctx);

        var request = client.ReceivedRequests[0];
        Assert.NotNull(request.Tools);
        Assert.Contains(request.Tools!, t => t.Name == "transfer_to_agent");
    }

    [Fact]
    public async Task Agent_with_disabled_handoff_does_not_get_transfer_tool()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-1",
            Provider = "openai",
            Model = "gpt-4",
            HandoffTargets = ["agent-b"],
            HandoffPolicy = HandoffPolicy.Disabled
        };

        var (step, client, _, _, _) = Setup(agent, [FinalResponse()]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Do something"
        });

        await step.ExecuteAsync(ctx);

        var request = client.ReceivedRequests[0];
        // No tools should be present (no explicit tools, no auto-injected ones)
        Assert.Null(request.Tools);
    }

    [Fact]
    public async Task Agent_with_empty_handoff_targets_does_not_get_transfer_tool()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-1",
            Provider = "openai",
            Model = "gpt-4",
            HandoffTargets = []
        };

        var (step, client, _, _, _) = Setup(agent, [FinalResponse()]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Do something"
        });

        await step.ExecuteAsync(ctx);

        Assert.Null(client.ReceivedRequests[0].Tools);
    }

    // ── Auto-inject delegate_to_agent tool ──

    [Fact]
    public async Task Supervisor_agent_gets_delegate_tool_injected()
    {
        var agent = new AgentDefinition
        {
            Id = "supervisor",
            Provider = "openai",
            Model = "gpt-4",
            SupervisorWorkers = ["worker-1", "worker-2"],
            DelegationPolicy = DelegationPolicy.Allowed
        };

        var (step, client, _, _, _) = Setup(agent, [FinalResponse()]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "supervisor",
            ["userPrompt"] = "Manage tasks"
        });

        await step.ExecuteAsync(ctx);

        var request = client.ReceivedRequests[0];
        Assert.NotNull(request.Tools);
        Assert.Contains(request.Tools!, t => t.Name == "delegate_to_agent");
    }

    [Fact]
    public async Task Supervisor_with_disabled_delegation_does_not_get_delegate_tool()
    {
        var agent = new AgentDefinition
        {
            Id = "supervisor",
            Provider = "openai",
            Model = "gpt-4",
            SupervisorWorkers = ["worker-1"],
            DelegationPolicy = DelegationPolicy.Disabled
        };

        var (step, client, _, _, _) = Setup(agent, [FinalResponse()]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "supervisor",
            ["userPrompt"] = "Manage"
        });

        await step.ExecuteAsync(ctx);

        Assert.Null(client.ReceivedRequests[0].Tools);
    }

    // ── Handoff interception ──

    [Fact]
    public async Task Transfer_tool_call_produces_handoff_result()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-a",
            Provider = "openai",
            Model = "gpt-4",
            HandoffTargets = ["agent-b"],
            HandoffPolicy = HandoffPolicy.Allowed,
            ConversationScope = ConversationScope.Handoff
        };

        var transferResponse = ToolCallResponse(
            ("tc-1", "transfer_to_agent", new Dictionary<string, object?>
            {
                ["target_agent"] = "agent-b",
                ["intent"] = "implement",
                ["context"] = "Build the feature"
            }));

        var (step, _, _, _, eventSink) = Setup(agent, [transferResponse]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-a",
            ["userPrompt"] = "Research and hand off"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Handoff, result.Status);
        Assert.NotNull(result.Handoff);
        Assert.Equal("agent-a", result.Handoff!.FromAgent);
        Assert.Equal("agent-b", result.Handoff.ToAgent);
        Assert.Equal("implement", result.Handoff.Intent);
        Assert.Equal(ConversationScope.Handoff, result.Handoff.ConversationScope);
        Assert.Null(result.Handoff.TransferredMessages); // Handoff scope = no messages

        // Verify handoff event was emitted
        var handoffEvents = eventSink.Events.OfType<AgentHandoffEvent>().ToList();
        Assert.Single(handoffEvents);
        Assert.Equal("agent-a", handoffEvents[0].FromAgent);
        Assert.Equal("agent-b", handoffEvents[0].ToAgent);
    }

    [Fact]
    public async Task Transfer_with_full_conversation_scope_transfers_messages()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-a",
            Provider = "openai",
            Model = "gpt-4",
            HandoffTargets = ["agent-b"],
            ConversationScope = ConversationScope.Full
        };

        var transferResponse = ToolCallResponse(
            ("tc-1", "transfer_to_agent", new Dictionary<string, object?>
            {
                ["target_agent"] = "agent-b",
                ["intent"] = "continue"
            }));

        var (step, _, _, _, _) = Setup(agent, [transferResponse]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-a",
            ["userPrompt"] = "Hello"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Handoff, result.Status);
        Assert.NotNull(result.Handoff!.TransferredMessages);
        Assert.NotEmpty(result.Handoff.TransferredMessages!);
    }

    [Fact]
    public async Task Transfer_with_lastN_scope_transfers_limited_messages()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-a",
            Provider = "openai",
            Model = "gpt-4",
            HandoffTargets = ["agent-b"],
            ConversationScope = ConversationScope.LastN,
            MaxContextMessages = 2
        };

        var transferResponse = ToolCallResponse(
            ("tc-1", "transfer_to_agent", new Dictionary<string, object?>
            {
                ["target_agent"] = "agent-b",
                ["intent"] = "continue"
            }));

        var (step, _, _, _, _) = Setup(agent, [transferResponse]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-a",
            ["userPrompt"] = "Hello"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Handoff, result.Status);
        Assert.NotNull(result.Handoff!.TransferredMessages);
        // Only user prompt message exists, so ≤ 2
        Assert.True(result.Handoff.TransferredMessages!.Count <= 2);
    }

    // ── Handoff blocked ──

    [Fact]
    public async Task Handoff_blocked_by_unknown_target_continues_loop()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-a",
            Provider = "openai",
            Model = "gpt-4",
            HandoffTargets = ["agent-b"]
        };

        var transferResponse = ToolCallResponse(
            ("tc-1", "transfer_to_agent", new Dictionary<string, object?>
            {
                ["target_agent"] = "agent-unknown",
                ["intent"] = "something"
            }));

        var (step, _, _, _, eventSink) = Setup(agent,
            [transferResponse, FinalResponse("Handled it myself")]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-a",
            ["userPrompt"] = "Try handoff"
        });

        var result = await step.ExecuteAsync(ctx);

        // Should succeed because LLM continued after blocked handoff
        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal("Handled it myself", result.Outputs["response"]);

        // Blocked event should be emitted
        var blockedEvents = eventSink.Events.OfType<AgentHandoffBlockedEvent>().ToList();
        Assert.Single(blockedEvents);
        Assert.Equal("agent-unknown", blockedEvents[0].ToAgent);
    }

    [Fact]
    public async Task Handoff_blocked_by_cycle_denial_continues_loop()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-a",
            Provider = "openai",
            Model = "gpt-4",
            HandoffTargets = ["agent-a"], // trying to hand off to self
            CyclePolicy = CyclePolicy.Deny
        };

        // First the LLM tries handoff to itself, gets blocked,
        // then continues and produces final answer
        var transferResponse = ToolCallResponse(
            ("tc-1", "transfer_to_agent", new Dictionary<string, object?>
            {
                ["target_agent"] = "agent-a",
                ["intent"] = "retry"
            }));

        var (step, _, _, _, eventSink) = Setup(agent,
            [transferResponse, FinalResponse("Did it myself")]);

        // Pre-populate visited agents to trigger cycle detection
        var execCtx = new AgentExecutionContext();
        execCtx.VisitedAgents.Add("agent-a");

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-a",
            ["userPrompt"] = "Do the thing",
            ["__agentExecutionContext"] = execCtx
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);

        var blockedEvents = eventSink.Events.OfType<AgentHandoffBlockedEvent>().ToList();
        Assert.Single(blockedEvents);
        Assert.Contains("visited", blockedEvents[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Transfer_without_target_agent_adds_error_and_continues()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-a",
            Provider = "openai",
            Model = "gpt-4",
            HandoffTargets = ["agent-b"]
        };

        var transferResponse = ToolCallResponse(
            ("tc-1", "transfer_to_agent", new Dictionary<string, object?>
            {
                ["intent"] = "something"
                // missing target_agent
            }));

        var (step, client, _, _, _) = Setup(agent,
            [transferResponse, FinalResponse("Recovered")]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-a",
            ["userPrompt"] = "Go"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal("Recovered", result.Outputs["response"]);

        // The second request should contain the error as a tool result message
        var secondRequest = client.ReceivedRequests[1];
        var toolMsg = secondRequest.Messages.FirstOrDefault(m => m.Role == LlmRole.Tool);
        Assert.NotNull(toolMsg);
        Assert.Contains("target_agent is required", toolMsg!.Content);
    }

    // ── Handoff result contains execution context ──

    [Fact]
    public async Task Handoff_result_includes_execution_context_in_outputs()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-a",
            Provider = "openai",
            Model = "gpt-4",
            HandoffTargets = ["agent-b"]
        };

        var transferResponse = ToolCallResponse(
            ("tc-1", "transfer_to_agent", new Dictionary<string, object?>
            {
                ["target_agent"] = "agent-b",
                ["intent"] = "implement"
            }));

        var (step, _, _, _, _) = Setup(agent, [transferResponse]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-a",
            ["userPrompt"] = "Hand off"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Handoff, result.Status);
        Assert.True(result.Outputs.ContainsKey("__agentExecutionContext"));
        var execCtx = result.Outputs["__agentExecutionContext"] as AgentExecutionContext;
        Assert.NotNull(execCtx);
        Assert.Equal(1, execCtx!.ChainDepth);
        Assert.Contains("agent-a", execCtx.VisitedAgents);
        Assert.Single(execCtx.HandoffHistory);
    }

    // ── Handoff with constraints ──

    [Fact]
    public async Task Handoff_passes_constraints()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-a",
            Provider = "openai",
            Model = "gpt-4",
            HandoffTargets = ["agent-b"]
        };

        var transferResponse = ToolCallResponse(
            ("tc-1", "transfer_to_agent", new Dictionary<string, object?>
            {
                ["target_agent"] = "agent-b",
                ["intent"] = "implement",
                ["constraints"] = "must use Python, must include tests"
            }));

        var (step, _, _, _, _) = Setup(agent, [transferResponse]);

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-a",
            ["userPrompt"] = "Research"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Handoff, result.Status);
        Assert.Equal(2, result.Handoff!.Constraints.Count);
        Assert.Contains("must use Python", result.Handoff.Constraints);
        Assert.Contains("must include tests", result.Handoff.Constraints);
    }

    // ── Escalation on max iterations ──

    [Fact]
    public async Task Max_iterations_with_escalation_target_produces_handoff()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-a",
            Provider = "openai",
            Model = "gpt-4",
            EscalationTarget = "agent-b",
            HandoffTargets = ["agent-b"]
        };

        // All responses are tool calls — will never end naturally
        var responses = Enumerable.Range(0, 5)
            .Select(i => ToolCallResponse(($"tc-{i}", "search", new Dictionary<string, object?>())))
            .ToList();

        var (step, _, _, toolReg, eventSink) = Setup(agent, responses);
        toolReg.Register(new FakeTool { Name = "search" });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-a",
            ["userPrompt"] = "Go",
            ["tools"] = new[] { "search" },
            ["maxIterations"] = 2
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Handoff, result.Status);
        Assert.NotNull(result.Handoff);
        Assert.Equal("agent-b", result.Handoff!.ToAgent);
        Assert.Contains("escalation", result.Handoff.Intent);

        // Escalation event should be emitted
        var escalationEvents = eventSink.Events.OfType<AgentEscalationEvent>().ToList();
        Assert.Single(escalationEvents);
        Assert.Equal("agent-a", escalationEvents[0].FailedAgent);
        Assert.Equal("agent-b", escalationEvents[0].EscalationTarget);
    }

    [Fact]
    public async Task Max_iterations_with_human_escalation_throws_interrupt()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-a",
            Provider = "openai",
            Model = "gpt-4",
            EscalationTarget = "human"
        };

        var responses = Enumerable.Range(0, 5)
            .Select(i => ToolCallResponse(($"tc-{i}", "search", new Dictionary<string, object?>())))
            .ToList();

        var (step, _, _, toolReg, _) = Setup(agent, responses);
        toolReg.Register(new FakeTool { Name = "search" });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-a",
            ["userPrompt"] = "Go",
            ["tools"] = new[] { "search" },
            ["maxIterations"] = 2
        });

        // No interrupt handler → InterruptException should be thrown
        await Assert.ThrowsAsync<Contracts.Interrupts.InterruptException>(
            () => step.ExecuteAsync(ctx));
    }

    // ── Transfer tool coexists with user tools ──

    [Fact]
    public async Task Transfer_tool_coexists_with_explicit_tools()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-a",
            Provider = "openai",
            Model = "gpt-4",
            HandoffTargets = ["agent-b"]
        };

        var (step, client, _, toolReg, _) = Setup(agent, [FinalResponse()]);
        toolReg.Register(new FakeTool { Name = "search" });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-a",
            ["userPrompt"] = "Go",
            ["tools"] = new[] { "search" }
        });

        await step.ExecuteAsync(ctx);

        var request = client.ReceivedRequests[0];
        Assert.NotNull(request.Tools);
        Assert.Contains(request.Tools!, t => t.Name == "search");
        Assert.Contains(request.Tools!, t => t.Name == "transfer_to_agent");
        Assert.Equal(2, request.Tools!.Count);
    }

    // ── Mixed tool calls: transfer + regular tools ──

    [Fact]
    public async Task Transfer_call_among_regular_calls_only_intercepts_transfer()
    {
        var agent = new AgentDefinition
        {
            Id = "agent-a",
            Provider = "openai",
            Model = "gpt-4",
            HandoffTargets = ["agent-b"]
        };

        // LLM returns both a regular tool call and a transfer in the same response
        var mixedResponse = new LlmResponse
        {
            Content = "",
            Success = true,
            Model = "gpt-4",
            InputTokens = 10,
            OutputTokens = 5,
            ToolCalls =
            [
                new ToolCall
                {
                    Id = "tc-1",
                    Name = "search",
                    Arguments = new() { ["q"] = "test" }
                },
                new ToolCall
                {
                    Id = "tc-2",
                    Name = "transfer_to_agent",
                    Arguments = new()
                    {
                        ["target_agent"] = "agent-b",
                        ["intent"] = "continue"
                    }
                }
            ]
        };

        var (step, _, _, toolReg, _) = Setup(agent, [mixedResponse]);
        toolReg.Register(new FakeTool { Name = "search" });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-a",
            ["userPrompt"] = "Research and hand off",
            ["tools"] = new[] { "search" }
        });

        var result = await step.ExecuteAsync(ctx);

        // Transfer should be intercepted → handoff result
        Assert.Equal(StepStatus.Handoff, result.Status);
        Assert.Equal("agent-b", result.Handoff!.ToAgent);
    }
}