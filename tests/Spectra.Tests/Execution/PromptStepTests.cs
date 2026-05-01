using Spectra.Contracts.Execution;
using Spectra.Contracts.Prompts;
using Spectra.Contracts.Providers;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Execution;
using Spectra.Kernel.Prompts;
using Xunit;

namespace Spectra.Tests.Execution;

public class PromptStepTests
{
    // ── Fakes ──────────────────────────────────────────────────────

    private class FakeLlmClient : ILlmClient
    {
        public string ProviderName => "fake";
        public string ModelId => Model;
        public ModelCapabilities Capabilities => new();
        public string Model { get; init; } = "fake-model";

        public LlmResponse Response { get; set; } = new() { Content = "ok", Success = true };
        public Exception? ThrowOnComplete { get; set; }
        public LlmRequest? ReceivedRequest { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            ReceivedRequest = request;
            if (ThrowOnComplete is not null) throw ThrowOnComplete;
            return Task.FromResult(Response);
        }
    }

    private class FakeStreamClient : ILlmStreamClient
    {
        public string ProviderName => "fake-stream";
        public string ModelId => "fake-stream-model";
        public ModelCapabilities Capabilities => new();

        public List<string> Chunks { get; init; } = [];
        public LlmRequest? ReceivedRequest { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            ReceivedRequest = request;
            return Task.FromResult(new LlmResponse
            {
                Content = string.Join("", Chunks),
                Success = true,
                Model = ModelId
            });
        }

        public async IAsyncEnumerable<string> StreamAsync(
            LlmRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ReceivedRequest = request;
            foreach (var chunk in Chunks)
            {
                await Task.Yield();
                yield return chunk;
            }
        }
    }

    private class FakeProviderRegistry : IProviderRegistry
    {
        private readonly Dictionary<string, ILlmClient> _clients = new();
        public Func<AgentDefinition, ILlmClient?>? OnCreateClient { get; set; }

        public void RegisterClient(string agentId, ILlmClient client) => _clients[agentId] = client;

        public ILlmClient? CreateClient(AgentDefinition agent)
        {
            if (OnCreateClient is not null) return OnCreateClient(agent);
            return _clients.TryGetValue(agent.Id, out var c) ? c : null;
        }

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

    private class FakePromptRegistry : IPromptRegistry
    {
        private readonly Dictionary<string, PromptTemplate> _prompts = new();

        public void Register(PromptTemplate prompt) => _prompts[prompt.Id] = prompt;
        public PromptTemplate? GetPrompt(string promptId) =>
            _prompts.TryGetValue(promptId, out var p) ? p : null;
        public IReadOnlyList<PromptTemplate> GetAll() => _prompts.Values.ToList();
        public void Reload() { }
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static AgentDefinition CreateAgent(
        string id = "agent-1",
        string provider = "openai",
        string model = "gpt-4",
        string? systemPrompt = "You are a helpful assistant.",
        string? systemPromptRef = null) =>
        new()
        {
            Id = id,
            Provider = provider,
            Model = model,
            SystemPrompt = systemPrompt,
            SystemPromptRef = systemPromptRef
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

    private (PromptStep Step, FakeLlmClient Client) SetupWithAgent(
        AgentDefinition? agent = null,
        LlmResponse? response = null)
    {
        agent ??= CreateAgent();
        var client = new FakeLlmClient
        {
            Model = agent.Model,
            Response = response ?? new LlmResponse { Content = "ok", Success = true, Model = agent.Model }
        };

        var agentReg = new FakeAgentRegistry();
        agentReg.Register(agent);

        var providerReg = new FakeProviderRegistry();
        providerReg.RegisterClient(agent.Id, client);

        var step = new PromptStep(providerReg, agentReg, new PromptRenderer());
        return (step, client);
    }

    // ── Happy Path ────────────────────────────────────────────────

    [Fact]
    public async Task Executes_completion_with_agent_id()
    {
        var (step, client) = SetupWithAgent(
            response: new LlmResponse
            {
                Content = "Hello, world!",
                Success = true,
                Model = "gpt-4",
                InputTokens = 10,
                OutputTokens = 5
            });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Say hello"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal("Hello, world!", result.Outputs["response"]);
        Assert.Equal("gpt-4", result.Outputs["model"]);
        Assert.Equal(10, result.Outputs["inputTokens"]);
        Assert.Equal(5, result.Outputs["outputTokens"]);
    }

    [Fact]
    public async Task Executes_completion_with_direct_provider_and_model()
    {
        var client = new FakeLlmClient
        {
            Response = new LlmResponse { Content = "Direct response", Success = true, Model = "mistral-7b" }
        };

        var providerReg = new FakeProviderRegistry
        {
            OnCreateClient = agent => agent.Provider == "mistral" && agent.Model == "mistral-7b" ? client : null
        };

        var step = new PromptStep(providerReg, new FakeAgentRegistry(), new PromptRenderer());
        var ctx = CreateContext(new()
        {
            ["provider"] = "mistral",
            ["model"] = "mistral-7b",
            ["userPrompt"] = "Hello"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal("Direct response", result.Outputs["response"]);
    }

    [Fact]
    public async Task Uses_agent_system_prompt_by_default()
    {
        var (step, client) = SetupWithAgent();
        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Hi"
        });

        await step.ExecuteAsync(ctx);

        Assert.Equal("You are a helpful assistant.", client.ReceivedRequest!.SystemPrompt);
    }

    // ── Prompt Resolution ─────────────────────────────────────────

    [Fact]
    public async Task System_prompt_input_overrides_agent_default()
    {
        var (step, client) = SetupWithAgent();
        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["systemPrompt"] = "You are a pirate.",
            ["userPrompt"] = "Ahoy"
        });

        await step.ExecuteAsync(ctx);

        Assert.Equal("You are a pirate.", client.ReceivedRequest!.SystemPrompt);
    }

    [Fact]
    public async Task PromptId_resolves_system_prompt_from_registry()
    {
        var agent = new AgentDefinition { Id = "agent-1", Provider = "openai", Model = "gpt-4" };
        var client = new FakeLlmClient
        {
            Response = new LlmResponse { Content = "ok", Success = true }
        };

        var agentReg = new FakeAgentRegistry();
        agentReg.Register(agent);
        var providerReg = new FakeProviderRegistry();
        providerReg.RegisterClient(agent.Id, client);
        var promptReg = new FakePromptRegistry();
        promptReg.Register(new PromptTemplate
        {
            Id = "my-prompt",
            Content = "You are a {{role}} expert."
        });

        var step = new PromptStep(providerReg, agentReg, new PromptRenderer(), promptReg);
        var ctx = CreateContext(new()
        {
            ["agentId"] = agent.Id,
            ["promptId"] = "my-prompt",
            ["role"] = "coding",
            ["userPrompt"] = "Help me"
        });

        await step.ExecuteAsync(ctx);

        Assert.Equal("You are a coding expert.", client.ReceivedRequest!.SystemPrompt);
    }

    [Fact]
    public async Task SystemPromptRef_on_agent_resolves_from_prompt_registry()
    {
        var agent = CreateAgent(systemPrompt: null, systemPromptRef: "agent-system");
        var client = new FakeLlmClient
        {
            Response = new LlmResponse { Content = "ok", Success = true }
        };

        var agentReg = new FakeAgentRegistry();
        agentReg.Register(agent);
        var providerReg = new FakeProviderRegistry();
        providerReg.RegisterClient(agent.Id, client);
        var promptReg = new FakePromptRegistry();
        promptReg.Register(new PromptTemplate
        {
            Id = "agent-system",
            Content = "You are a {{domain}} specialist."
        });

        var step = new PromptStep(providerReg, agentReg, new PromptRenderer(), promptReg);
        var ctx = CreateContext(new()
        {
            ["agentId"] = agent.Id,
            ["domain"] = "security",
            ["userPrompt"] = "Audit this"
        });

        await step.ExecuteAsync(ctx);

        Assert.Equal("You are a security specialist.", client.ReceivedRequest!.SystemPrompt);
    }

    [Fact]
    public async Task Renders_variables_in_user_prompt()
    {
        var (step, client) = SetupWithAgent();
        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Summarize: {{document}}"
        });
        ctx.State.Context["document"] = "This is a test document.";

        await step.ExecuteAsync(ctx);

        var userMsg = client.ReceivedRequest!.Messages[0];
        Assert.Equal("Summarize: This is a test document.", userMsg.Content);
    }

    // ── Streaming ─────────────────────────────────────────────────

    [Fact]
    public async Task Streaming_invokes_on_token_callback()
    {
        var agent = CreateAgent();
        var streamClient = new FakeStreamClient { Chunks = ["Hello", ", ", "world", "!"] };

        var agentReg = new FakeAgentRegistry();
        agentReg.Register(agent);
        var providerReg = new FakeProviderRegistry();
        providerReg.RegisterClient(agent.Id, streamClient);

        var tokens = new List<string>();
        var step = new PromptStep(providerReg, agentReg, new PromptRenderer());
        var ctx = CreateContext(
            new() { ["agentId"] = agent.Id, ["userPrompt"] = "Stream test" },
            onToken: (token, ct) => { tokens.Add(token); return Task.CompletedTask; });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal("Hello, world!", result.Outputs["response"]);
        Assert.Equal(["Hello", ", ", "world", "!"], tokens);
    }

    [Fact]
    public async Task Non_streaming_client_falls_back_to_complete()
    {
        var (step, client) = SetupWithAgent();
        // OnToken is set, but client is not ILlmStreamClient → should fall back
        var ctx = CreateContext(
            new() { ["agentId"] = "agent-1", ["userPrompt"] = "Test" },
            onToken: (_, _) => Task.CompletedTask);

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.NotNull(client.ReceivedRequest);
    }

    // ── Multimodal ────────────────────────────────────────────────

    [Fact]
    public async Task Images_build_content_parts_on_user_message()
    {
        var (step, client) = SetupWithAgent(
            response: new LlmResponse { Content = "I see an image", Success = true });

        var images = new List<Dictionary<string, object?>>
        {
            new() { ["data"] = "base64data==", ["mimeType"] = "image/jpeg" }
        };
        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "What is in this image?",
            ["images"] = images
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        var userMsg = client.ReceivedRequest!.Messages[0];
        Assert.NotNull(userMsg.ContentParts);
        Assert.Equal(2, userMsg.ContentParts!.Count);
        Assert.True(userMsg.HasMedia);
    }

    // ── Output Mode ───────────────────────────────────────────────

    [Fact]
    public async Task OutputMode_json_sets_request_mode()
    {
        var (step, client) = SetupWithAgent();
        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Return JSON",
            ["outputMode"] = "json"
        });

        await step.ExecuteAsync(ctx);

        Assert.Equal(LlmOutputMode.Json, client.ReceivedRequest!.OutputMode);
    }

    // ── Error Cases ───────────────────────────────────────────────

    [Fact]
    public async Task Missing_agent_and_provider_returns_fail()
    {
        var step = new PromptStep(
            new FakeProviderRegistry(), new FakeAgentRegistry(), new PromptRenderer());
        var ctx = CreateContext(new() { ["userPrompt"] = "Hello" });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("agentId", result.ErrorMessage);
    }

    [Fact]
    public async Task Unknown_agent_returns_fail()
    {
        var step = new PromptStep(
            new FakeProviderRegistry(), new FakeAgentRegistry(), new PromptRenderer());
        var ctx = CreateContext(new()
        {
            ["agentId"] = "nonexistent",
            ["userPrompt"] = "Hello"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("nonexistent", result.ErrorMessage);
    }

    [Fact]
    public async Task Llm_error_response_returns_fail()
    {
        var (step, _) = SetupWithAgent(
            response: LlmResponse.Error("Rate limit exceeded"));

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Hello"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("Rate limit", result.ErrorMessage);
    }

    [Fact]
    public async Task Llm_exception_returns_fail_with_exception()
    {
        var agent = CreateAgent();
        var client = new FakeLlmClient
        {
            ThrowOnComplete = new HttpRequestException("Connection refused")
        };

        var agentReg = new FakeAgentRegistry();
        agentReg.Register(agent);
        var providerReg = new FakeProviderRegistry();
        providerReg.RegisterClient(agent.Id, client);

        var step = new PromptStep(providerReg, agentReg, new PromptRenderer());
        var ctx = CreateContext(new()
        {
            ["agentId"] = agent.Id,
            ["userPrompt"] = "Hello"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("Connection refused", result.ErrorMessage);
        Assert.NotNull(result.Exception);
    }

    // ── Integration: namespaced template rendering ────────────────

    [Fact]
    public async Task Prompt_template_with_inputs_reference_renders_correctly()
    {
        var (step, client) = SetupWithAgent();
        var state = new WorkflowState { WorkflowId = "wf-1" };
        state.Inputs["request"] = "Summarize the codebase";

        var ctx = CreateContext(
            new()
            {
                ["agentId"] = "agent-1",
                ["userPrompt"] = "Please do: {{inputs.request}}"
            },
            state: state);

        await step.ExecuteAsync(ctx);

        Assert.NotNull(client.ReceivedRequest);
        var userMsg = client.ReceivedRequest!.Messages[0];
        Assert.Equal("Please do: Summarize the codebase", userMsg.Content);
    }

    [Fact]
    public async Task Prompt_template_with_nodes_reference_renders_correctly()
    {
        var (step, client) = SetupWithAgent();
        var state = new WorkflowState { WorkflowId = "wf-1" };
        state.Nodes["flat-render"] = new Dictionary<string, object?>
        {
            ["tree"] = "rendered-tree-content"
        };

        var ctx = CreateContext(
            new()
            {
                ["agentId"] = "agent-1",
                ["userPrompt"] = "Use this tree: {{nodes.flat-render.tree}}"
            },
            state: state);

        await step.ExecuteAsync(ctx);

        Assert.NotNull(client.ReceivedRequest);
        var userMsg = client.ReceivedRequest!.Messages[0];
        Assert.Equal("Use this tree: rendered-tree-content", userMsg.Content);
    }
}