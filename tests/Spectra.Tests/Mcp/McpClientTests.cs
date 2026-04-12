using System.Text.Json;
using System.Threading.Channels;
using Spectra.Contracts.Mcp;
using Spectra.Kernel.Mcp;
using Xunit;

namespace Spectra.Tests.Mcp;

public class McpClientTests
{
    /// <summary>
    /// Fake transport that feeds scripted responses and captures sent messages.
    /// </summary>
    private sealed class FakeTransport : IMcpTransport
    {
        private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>();
        private readonly List<string> _sent = [];
        private readonly object _lock = new();

        public bool IsConnected { get; set; } = true;

        public IReadOnlyList<string> SentMessages
        {
            get { lock (_lock) return _sent.ToList(); }
        }

        /// <summary>Enqueue a response the client will receive.</summary>
        public void EnqueueResponse(string json) => _inbound.Writer.TryWrite(json);

        /// <summary>Enqueue a JSON-RPC response for the given request id.</summary>
        public void EnqueueResult(int id, object result)
        {
            var json = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                result
            }, SerializerOptions);
            EnqueueResponse(json);
        }

        public void EnqueueError(int id, int code, string message)
        {
            var json = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                error = new { code, message }
            }, SerializerOptions);
            EnqueueResponse(json);
        }

        public Task SendAsync(string jsonRpcMessage, CancellationToken ct = default)
        {
            lock (_lock) _sent.Add(jsonRpcMessage);

            // Auto-respond based on method for the handshake
            var doc = JsonDocument.Parse(jsonRpcMessage);
            if (doc.RootElement.TryGetProperty("id", out var idProp)
                && doc.RootElement.TryGetProperty("method", out var methodProp))
            {
                var method = methodProp.GetString();
                var reqId = idProp.GetInt32();

                // Schedule response delivery
                Task.Run(async () =>
                {
                    await Task.Delay(5, ct); // tiny delay to simulate I/O
                    // Don't auto-respond — let tests control responses
                });
            }

            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveAsync(CancellationToken ct = default)
        {
            try
            {
                return await _inbound.Reader.ReadAsync(ct);
            }
            catch (ChannelClosedException) { return null; }
            catch (OperationCanceledException) { return null; }
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            _inbound.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    private static McpServerConfig DefaultConfig(string name = "test") => new()
    {
        Name = name,
        Command = "echo",
        Transport = McpTransportType.Stdio
    };

    private static McpClient CreateClient(FakeTransport transport, string name = "test")
        => new(transport, DefaultConfig(name));

    /// <summary>
    /// Waits for the specified sent message, parses its JSON-RPC id, and returns it.
    /// This eliminates hardcoded id assumptions that cause race-dependent failures.
    /// </summary>
    private static async Task<int> WaitAndGetRequestId(FakeTransport transport, int messageIndex)
    {
        await WaitForSentCount(transport, messageIndex + 1);
        var doc = JsonDocument.Parse(transport.SentMessages[messageIndex]);
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    /// <summary>
    /// Responds to the standard initialize + tools/list handshake by parsing
    /// actual request ids from sent messages instead of hardcoding them.
    /// </summary>
    private static async Task RespondToHandshake(
        FakeTransport transport,
        string name = "test",
        List<McpToolInfo>? tools = null)
    {
        // Respond to initialize request
        var initId = await WaitAndGetRequestId(transport, 0);
        transport.EnqueueResult(initId, new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            serverInfo = new { name, version = "1.0.0" }
        });

        // Wait for initialized notification (index 1) + tools/list request (index 2)
        await WaitForSentCount(transport, 3);
        var toolsMsg = JsonDocument.Parse(transport.SentMessages[2]);
        var toolsId = toolsMsg.RootElement.GetProperty("id").GetInt32();
        transport.EnqueueResult(toolsId, new
        {
            tools = tools ?? new List<McpToolInfo>()
        });
    }

    /// <summary>
    /// Runs InitializeAsync against a FakeTransport by scheduling the expected
    /// responses (initialize result, tools/list result) before the call.
    /// </summary>
    private static async Task<McpClient> CreateInitializedClientAsync(
        FakeTransport transport,
        List<McpToolInfo>? tools = null,
        string name = "test")
    {
        var client = CreateClient(transport, name);

        _ = Task.Run(() => RespondToHandshake(transport, name, tools));

        await client.InitializeAsync();
        return client;
    }

    private static async Task WaitForSentCount(FakeTransport transport, int count)
    {
        for (var i = 0; i < 200; i++) // up to 2 seconds
        {
            if (transport.SentMessages.Count >= count) return;
            await Task.Delay(10);
        }
    }

    // ── Initialization ──

    [Fact]
    public async Task InitializeAsync_SendsInitializeRequest()
    {
        var transport = new FakeTransport();
        var client = CreateClient(transport);

        _ = Task.Run(() => RespondToHandshake(transport));

        await client.InitializeAsync();

        var firstMessage = transport.SentMessages[0];
        var doc = JsonDocument.Parse(firstMessage);
        Assert.Equal("initialize", doc.RootElement.GetProperty("method").GetString());

        await client.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_SendsInitializedNotification()
    {
        var transport = new FakeTransport();
        var client = CreateClient(transport);

        _ = Task.Run(() => RespondToHandshake(transport));

        await client.InitializeAsync();

        // Second message should be the notification
        var notification = transport.SentMessages[1];
        var doc = JsonDocument.Parse(notification);
        Assert.Equal("notifications/initialized", doc.RootElement.GetProperty("method").GetString());
        // Notifications have no id
        Assert.False(doc.RootElement.TryGetProperty("id", out var idProp) && idProp.ValueKind != JsonValueKind.Null);

        await client.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_ThrowsOnDoubleInit()
    {
        var transport = new FakeTransport();
        var client = await CreateInitializedClientAsync(transport);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.InitializeAsync());

        await client.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_FailedHandshake_Throws()
    {
        var transport = new FakeTransport();
        var client = CreateClient(transport);

        _ = Task.Run(async () =>
        {
            var initId = await WaitAndGetRequestId(transport, 0);
            transport.EnqueueError(initId, -32600, "Invalid request");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.InitializeAsync());

        await client.DisposeAsync();
    }

    // ── Tool discovery ──

    [Fact]
    public async Task InitializeAsync_DiscoverTools_PopulatesToolsList()
    {
        var transport = new FakeTransport();
        var client = CreateClient(transport);

        _ = Task.Run(async () =>
        {
            var initId = await WaitAndGetRequestId(transport, 0);
            transport.EnqueueResult(initId, new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                serverInfo = new { name = "test", version = "1.0" }
            });

            await WaitForSentCount(transport, 3);
            var toolsMsg = JsonDocument.Parse(transport.SentMessages[2]);
            var toolsId = toolsMsg.RootElement.GetProperty("id").GetInt32();
            transport.EnqueueResult(toolsId, new
            {
                tools = new[]
                {
                    new { name = "read_file", description = "Read a file" },
                    new { name = "write_file", description = "Write a file" }
                }
            });
        });

        await client.InitializeAsync();

        Assert.Equal(2, client.Tools.Count);
        Assert.Equal("read_file", client.Tools[0].Name);
        Assert.Equal("write_file", client.Tools[1].Name);

        await client.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_EmptyToolList_IsOk()
    {
        var transport = new FakeTransport();
        var client = await CreateInitializedClientAsync(transport);

        Assert.Empty(client.Tools);

        await client.DisposeAsync();
    }

    // ── CallToolAsync ──

    [Fact]
    public async Task CallToolAsync_ThrowsWhenNotInitialized()
    {
        var transport = new FakeTransport();
        var client = CreateClient(transport);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CallToolAsync("read_file", new Dictionary<string, object?>()));

        await client.DisposeAsync();
    }

    [Fact]
    public async Task CallToolAsync_SendsCorrectRequest()
    {
        var transport = new FakeTransport();
        var client = await CreateInitializedClientAsync(transport);

        var initMessageCount = transport.SentMessages.Count;

        _ = Task.Run(async () =>
        {
            // Wait for the tool call message
            await WaitForSentCount(transport, initMessageCount + 1);
            var callMsg = JsonDocument.Parse(transport.SentMessages[initMessageCount]);
            var callId = callMsg.RootElement.GetProperty("id").GetInt32();
            transport.EnqueueResult(callId, new
            {
                content = new[] { new { type = "text", text = "file contents" } }
            });
        });

        var result = await client.CallToolAsync("read_file", new Dictionary<string, object?>
        {
            ["path"] = "/tmp/test.txt"
        });

        Assert.True(result.IsSuccess);

        // Verify the request
        var callMessage = transport.SentMessages[initMessageCount];
        var doc = JsonDocument.Parse(callMessage);
        Assert.Equal("tools/call", doc.RootElement.GetProperty("method").GetString());

        await client.DisposeAsync();
    }

    [Fact]
    public async Task CallToolAsync_ReturnsErrorResponse()
    {
        var transport = new FakeTransport();
        var client = await CreateInitializedClientAsync(transport);

        var initMessageCount = transport.SentMessages.Count;

        _ = Task.Run(async () =>
        {
            await WaitForSentCount(transport, initMessageCount + 1);
            var callMsg = JsonDocument.Parse(transport.SentMessages[initMessageCount]);
            var callId = callMsg.RootElement.GetProperty("id").GetInt32();
            transport.EnqueueError(callId, -32000, "Tool execution failed");
        });

        var result = await client.CallToolAsync("broken_tool", new Dictionary<string, object?>());

        Assert.False(result.IsSuccess);
        Assert.Equal(-32000, result.Error!.Code);

        await client.DisposeAsync();
    }

    // ── Disposal ──

    [Fact]
    public async Task DisposeAsync_DisposesTransport()
    {
        var transport = new FakeTransport();
        var client = await CreateInitializedClientAsync(transport);

        await client.DisposeAsync();

        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var transport = new FakeTransport();
        var client = await CreateInitializedClientAsync(transport);

        await client.DisposeAsync();
        await client.DisposeAsync(); // second call should not throw
    }

    // ── ServerName ──

    [Fact]
    public void ServerName_ReturnsConfigName()
    {
        var transport = new FakeTransport();
        var client = CreateClient(transport, "my-server");

        Assert.Equal("my-server", client.ServerName);
    }
}