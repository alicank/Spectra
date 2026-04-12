using System.Text.Json;
using System.Threading.Channels;
using Spectra.Contracts.Events;
using Spectra.Contracts.Mcp;
using Spectra.Contracts.State;
using Spectra.Contracts.Tools;
using Spectra.Kernel.Mcp;
using Xunit;

namespace Spectra.Tests.Mcp;

public class McpToolAdapterTests
{
    // ── Test infrastructure ──

    private sealed class FakeTransport : IMcpTransport
    {
        private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>();
        public bool IsConnected { get; set; } = true;
        public void EnqueueResponse(string json) => _inbound.Writer.TryWrite(json);

        public Task SendAsync(string msg, CancellationToken ct = default) => Task.CompletedTask;
        public async Task<string?> ReceiveAsync(CancellationToken ct = default)
        {
            try { return await _inbound.Reader.ReadAsync(ct); }
            catch { return null; }
        }
        public ValueTask DisposeAsync() { _inbound.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }

    private sealed class RecordingEventSink : IEventSink
    {
        public List<WorkflowEvent> Events { get; } = [];
        public Task PublishAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }
    }

    private static WorkflowState CreateState(string runId = "run-1", string workflowId = "wf-1")
    {
        var state = new WorkflowState
        {
            RunId = runId,
            WorkflowId = workflowId
        };
        return state;
    }

    private static McpToolInfo CreateToolInfo(
        string name = "read_file",
        bool readOnlyHint = true) => new()
        {
            Name = name,
            Description = $"Test tool {name}",
            Annotations = new McpToolAnnotations { ReadOnlyHint = readOnlyHint }
        };

    private static McpServerConfig CreateConfig(
        string name = "test",
        bool readOnly = false,
        int maxCalls = 0,
        decimal costPerCall = 0,
        bool requireApproval = false,
        int maxResponseSize = 1_048_576) => new()
        {
            Name = name,
            Command = "echo",
            ReadOnly = readOnly,
            MaxCallsPerSession = maxCalls,
            CostPerCall = costPerCall,
            RequireApproval = requireApproval,
            MaxResponseSizeBytes = maxResponseSize,
            Resilience = new McpResilienceOptions { MaxRetries = 0, Timeout = TimeSpan.FromSeconds(5) }
        };

    // ── Name and Definition ──

    [Fact]
    public void Name_UsesPrefixedConvention()
    {
        var adapter = CreateAdapter("filesystem", "read_file");

        Assert.Equal("mcp__filesystem__read_file", adapter.Name);
    }

    [Fact]
    public void Definition_MapsFromMcpToolInfo()
    {
        var adapter = CreateAdapter("fs", "read_file");

        Assert.Equal("mcp__fs__read_file", adapter.Definition.Name);
        Assert.Contains("read_file", adapter.Definition.Description);
    }

    [Fact]
    public void ServerName_ExposesConfigName()
    {
        var adapter = CreateAdapter("my-server", "tool1");

        Assert.Equal("my-server", adapter.ServerName);
    }

    [Fact]
    public void McpToolName_ExposesRawName()
    {
        var adapter = CreateAdapter("server", "my_tool");

        Assert.Equal("my_tool", adapter.McpToolName);
    }

    // ── Guardrail: read-only ──

    [Fact]
    public async Task ExecuteAsync_ReadOnlyPolicy_BlocksWriteTools()
    {
        var config = CreateConfig(readOnly: true);
        var toolInfo = CreateToolInfo("write_file", readOnlyHint: false);
        var adapter = CreateAdapter("s", toolInfo, config);

        var result = await adapter.ExecuteAsync(
            new Dictionary<string, object?>(), CreateState());

        Assert.False(result.Success);
        Assert.Contains("read-only policy", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_ReadOnlyPolicy_AllowsReadTools()
    {
        // This test would need a real McpClient that responds.
        // We test the guard logic by verifying the write tool is blocked.
        // Read tools pass the guard and proceed to the actual MCP call.
        var config = CreateConfig(readOnly: true);
        var toolInfo = CreateToolInfo("search", readOnlyHint: true);
        // Since we can't easily mock McpClient.CallToolAsync, just verify
        // the adapter doesn't block read tools at the guard level.
        var adapter = CreateAdapter("s", toolInfo, config);

        // This will fail at the MCP call level (fake client), not at the guard level
        // We just verify the guard didn't produce a "read-only policy" error
        var result = await adapter.ExecuteAsync(
            new Dictionary<string, object?>(), CreateState());

        // It may fail (timeout/connection), but NOT because of read-only
        if (!result.Success)
            Assert.DoesNotContain("read-only policy", result.Error ?? "");
    }

    // ── Guardrail: rate limit ──

    [Fact]
    public async Task ExecuteAsync_RateLimit_BlocksAfterMaxCalls()
    {
        var config = CreateConfig(maxCalls: 1);
        var tracker = new McpCallTracker();
        tracker.RecordCall(config, "tool1"); // already at limit

        var adapter = CreateAdapter("s", CreateToolInfo(), config, tracker);

        var result = await adapter.ExecuteAsync(
            new Dictionary<string, object?>(), CreateState());

        Assert.False(result.Success);
        Assert.Contains("maximum of 1 calls", result.Error);
    }

    // ── Guardrail: approval required ──

    [Fact]
    public async Task ExecuteAsync_ApprovalRequired_BlocksWithMessage()
    {
        var config = CreateConfig(requireApproval: true);
        var adapter = CreateAdapter("s", CreateToolInfo(), config);

        var result = await adapter.ExecuteAsync(
            new Dictionary<string, object?>(), CreateState());

        Assert.False(result.Success);
        Assert.Contains("requires human approval", result.Error);
    }

    // ── Events ──

    [Fact]
    public async Task ExecuteAsync_EmitsBlockedEvent_OnReadOnlyViolation()
    {
        var sink = new RecordingEventSink();
        var config = CreateConfig(readOnly: true);
        var toolInfo = CreateToolInfo("write_file", readOnlyHint: false);
        var adapter = CreateAdapter("s", toolInfo, config, eventSink: sink);

        await adapter.ExecuteAsync(new Dictionary<string, object?>(), CreateState());

        var blocked = sink.Events.OfType<McpToolCallBlockedEvent>().FirstOrDefault();
        Assert.NotNull(blocked);
        Assert.Equal("write_file", blocked!.ToolName);
        Assert.Contains("read_only_policy", blocked.Reason);
    }

    [Fact]
    public async Task ExecuteAsync_EmitsBlockedEvent_OnRateLimit()
    {
        var sink = new RecordingEventSink();
        var config = CreateConfig(maxCalls: 0); // 0 means unlimited for config...
        // Actually set it to 1
        var limitedConfig = new McpServerConfig
        {
            Name = "s",
            Command = "echo",
            MaxCallsPerSession = 1,
            Resilience = new McpResilienceOptions { MaxRetries = 0 }
        };
        var tracker = new McpCallTracker();
        tracker.RecordCall(limitedConfig, "tool1");

        var adapter = CreateAdapter("s", CreateToolInfo(), limitedConfig, tracker, sink);

        await adapter.ExecuteAsync(new Dictionary<string, object?>(), CreateState());

        Assert.Contains(sink.Events, e => e is McpToolCallBlockedEvent);
    }

    // ── ITool contract ──

    [Fact]
    public void Adapter_ImplementsITool()
    {
        var adapter = CreateAdapter("s", "tool");

        Assert.IsAssignableFrom<ITool>(adapter);
    }

    // ── Helpers ──

    private static McpToolAdapter CreateAdapter(
        string serverName,
        string toolName,
        McpServerConfig? config = null,
        McpCallTracker? tracker = null,
        IEventSink? eventSink = null)
    {
        return CreateAdapter(serverName, CreateToolInfo(toolName), config, tracker, eventSink);
    }

    private static McpToolAdapter CreateAdapter(
        string serverName,
        McpToolInfo toolInfo,
        McpServerConfig? config = null,
        McpCallTracker? tracker = null,
        IEventSink? eventSink = null)
    {
        config ??= CreateConfig(serverName);
        tracker ??= new McpCallTracker();

        // Create a fake McpClient (not initialized — calls will fail at transport level)
        var transport = new FakeTransport();
        var client = new McpClient(transport, config);

        return new McpToolAdapter(client, toolInfo, config, tracker, eventSink);
    }
}