using System.Text.Json;
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

public class StructuredOutputStepTests
{
    // ── Fakes ──────────────────────────────────────────────────────

    private class FakeLlmClient : ILlmClient
    {
        public string ProviderName => "fake";
        public string ModelId => "fake-model";
        public ModelCapabilities Capabilities => new();

        public LlmResponse Response { get; set; } = new() { Content = "ok", Success = true };
        public LlmRequest? ReceivedRequest { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            ReceivedRequest = request;
            return Task.FromResult(Response);
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

    // ── Helpers ────────────────────────────────────────────────────

    private static StepContext CreateContext(Dictionary<string, object?> inputs) =>
        new()
        {
            RunId = "run-1",
            WorkflowId = "wf-1",
            NodeId = "node-1",
            State = new WorkflowState { WorkflowId = "wf-1" },
            CancellationToken = CancellationToken.None,
            Inputs = inputs
        };

    private (StructuredOutputStep Step, FakeLlmClient Client) Setup(LlmResponse? response = null)
    {
        var agent = new AgentDefinition
        {
            Id = "agent-1",
            Provider = "openai",
            Model = "gpt-4",
            SystemPrompt = "You are a helpful assistant."
        };

        var client = new FakeLlmClient
        {
            Response = response ?? new LlmResponse
            {
                Content = """{"name":"Alice","age":30}""",
                Success = true,
                Model = "gpt-4"
            }
        };

        var agentReg = new FakeAgentRegistry();
        agentReg.Register(agent);
        var providerReg = new FakeProviderRegistry();
        providerReg.RegisterClient(agent.Id, client);

        var step = new StructuredOutputStep(providerReg, agentReg, new PromptRenderer());
        return (step, client);
    }

    // ── StepType ─────────────────────────────────────────────────

    [Fact]
    public void StepType_is_structured_output()
    {
        var (step, _) = Setup();
        Assert.Equal("structured_output", step.StepType);
    }

    // ── Happy Path ───────────────────────────────────────────────

    [Fact]
    public async Task Valid_json_response_returns_flat_dictionary()
    {
        var (step, _) = Setup();
        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Return a person object",
            ["jsonSchema"] = """{"type":"object","properties":{"name":{"type":"string"},"age":{"type":"integer"}}}"""
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);

        // Outputs are flat CLR types, not JsonElement
        Assert.Equal("Alice", result.Outputs["name"]);
        Assert.Equal(30, Convert.ToInt64(result.Outputs["age"])); // NormalizeJsonElement returns long for integers
    }

    [Fact]
    public async Task Outputs_do_not_contain_prompt_step_telemetry()
    {
        // The Pydantic-style step returns ONLY the parsed domain data.
        // Telemetry (model, tokens, latency) belongs in events, not workflow state.
        var (step, _) = Setup(new LlmResponse
        {
            Content = """{"result":"ok"}""",
            Success = true,
            Model = "gpt-4",
            InputTokens = 15,
            OutputTokens = 8
        });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Test"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);

        // Only the parsed domain field should be present
        Assert.Equal("ok", result.Outputs["result"]);

        // Telemetry keys from PromptStep should NOT leak into the flat output
        Assert.False(result.Outputs.ContainsKey("model"));
        Assert.False(result.Outputs.ContainsKey("inputTokens"));
        Assert.False(result.Outputs.ContainsKey("outputTokens"));
    }

    // ── Output Mode ──────────────────────────────────────────────

    [Fact]
    public async Task Always_uses_json_mode_even_with_schema()
    {
        // The Pydantic-style approach always uses Json mode (not StructuredJson).
        // Schema is injected into the prompt, validation is client-side.
        var (step, client) = Setup();
        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Return JSON",
            ["jsonSchema"] = """{"type":"object"}"""
        });

        await step.ExecuteAsync(ctx);

        Assert.Equal(LlmOutputMode.Json, client.ReceivedRequest!.OutputMode);
    }

    [Fact]
    public async Task Without_schema_uses_json_mode()
    {
        var (step, client) = Setup();
        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Return JSON"
        });

        await step.ExecuteAsync(ctx);

        Assert.Equal(LlmOutputMode.Json, client.ReceivedRequest!.OutputMode);
    }

    [Fact]
    public async Task Schema_is_injected_into_system_prompt()
    {
        var (step, client) = Setup();
        var schema = """{"type":"object","properties":{"x":{"type":"string"}}}""";
        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Return JSON",
            ["jsonSchema"] = schema
        });

        await step.ExecuteAsync(ctx);

        // The schema should appear in the system prompt sent to the LLM
        Assert.Contains(schema, client.ReceivedRequest!.SystemPrompt);
        Assert.Contains("valid JSON", client.ReceivedRequest.SystemPrompt);
    }

    // ── JSON Parsing Errors ──────────────────────────────────────

    [Fact]
    public async Task Invalid_json_response_returns_fail()
    {
        var (step, _) = Setup(new LlmResponse
        {
            Content = "This is not JSON at all",
            Success = true,
            Model = "gpt-4"
        });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Return JSON",
            ["jsonSchema"] = """{"type":"object"}"""
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("not valid JSON", result.ErrorMessage);
        Assert.IsAssignableFrom<JsonException>(result.Exception);
        Assert.Equal("This is not JSON at all", result.Outputs["rawResponse"]);
    }

    [Fact]
    public async Task Empty_response_returns_fail()
    {
        var (step, _) = Setup(new LlmResponse
        {
            Content = "",
            Success = true,
            Model = "gpt-4"
        });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Return JSON"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("empty response", result.ErrorMessage);
    }

    // ── LLM Failure Passthrough ──────────────────────────────────

    [Fact]
    public async Task Llm_failure_is_passed_through()
    {
        var (step, _) = Setup(LlmResponse.Error("Rate limit exceeded"));

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Return JSON"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("Rate limit", result.ErrorMessage);
    }

    // ── JSON Array Output ────────────────────────────────────────

    [Fact]
    public async Task Json_array_response_is_wrapped_in_result_key()
    {
        // When the root is an array (not an object), it gets wrapped in {"result": [...]}
        var (step, _) = Setup(new LlmResponse
        {
            Content = """[{"id":1},{"id":2}]""",
            Success = true,
            Model = "gpt-4"
        });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Return a list"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);

        // Array is wrapped under "result" key
        Assert.True(result.Outputs.ContainsKey("result"));
        var list = result.Outputs["result"] as List<object?>;
        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);

        // Each item is a normalized dictionary
        var first = list[0] as Dictionary<string, object?>;
        Assert.NotNull(first);
        Assert.Equal(1, Convert.ToInt64(first!["id"]));
    }

    // ── ExtractJson: markdown code block handling ─────────────────

    [Fact]
    public async Task Extracts_json_from_markdown_code_block()
    {
        var (step, _) = Setup(new LlmResponse
        {
            Content = """
                ```json
                {"name":"Bob","age":25}
                ```
                """,
            Success = true,
            Model = "gpt-4"
        });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Return JSON"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal("Bob", result.Outputs["name"]);
        Assert.Equal(25, Convert.ToInt64(result.Outputs["age"]));
    }

    [Fact]
    public async Task Extracts_json_array_from_markdown_code_block()
    {
        var (step, _) = Setup(new LlmResponse
        {
            Content = """
                ```json
                [{"id":1},{"id":2}]
                ```
                """,
            Success = true,
            Model = "gpt-4"
        });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Return a list"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        var list = result.Outputs["result"] as List<object?>;
        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
    }

    // ── NormalizeJsonElement: CLR type conversion ─────────────────

    [Fact]
    public async Task Normalizes_nested_objects_to_dictionaries()
    {
        var (step, _) = Setup(new LlmResponse
        {
            Content = """{"person":{"name":"Eve","active":true},"count":42}""",
            Success = true,
            Model = "gpt-4"
        });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Return nested JSON"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal(42, Convert.ToInt64(result.Outputs["count"]));
        var person = result.Outputs["person"] as Dictionary<string, object?>;
        Assert.NotNull(person);
        Assert.Equal("Eve", person!["name"]);
        Assert.Equal(true, person["active"]);
    }

    [Fact]
    public async Task Normalizes_boolean_and_null_values()
    {
        var (step, _) = Setup(new LlmResponse
        {
            Content = """{"enabled":true,"disabled":false,"missing":null}""",
            Success = true,
            Model = "gpt-4"
        });

        var ctx = CreateContext(new()
        {
            ["agentId"] = "agent-1",
            ["userPrompt"] = "Return JSON"
        });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal(true, result.Outputs["enabled"]);
        Assert.Equal(false, result.Outputs["disabled"]);
        Assert.Null(result.Outputs["missing"]);
    }
}