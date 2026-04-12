using System.Net;
using System.Text;
using Spectra.Contracts.Providers;
using Spectra.Extensions.Providers.Anthropic;
using Xunit;

namespace Spectra.Tests.Providers;

public class AnthropicClientTests
{
    // ─── fake HTTP handler ──────────────────────────────────────────

    private sealed class FakeHandler : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;

        public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
            => _send = send;

        public FakeHandler(HttpStatusCode status, string body)
            : this(_ => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            }))
        { }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => _send(request);
    }

    private static readonly string SuccessJson = """
        {
            "id": "msg_test123",
            "type": "message",
            "role": "assistant",
            "model": "claude-sonnet-4-20250514",
            "content": [{ "type": "text", "text": "Test response" }],
            "stop_reason": "end_turn",
            "usage": { "input_tokens": 5, "output_tokens": 3 }
        }
        """;

    private static AnthropicClient CreateClient(
        DelegatingHandler handler, AnthropicConfig? config = null)
    {
        config ??= new AnthropicConfig { ApiKey = "sk-ant-test", Model = "claude-sonnet-4-20250514" };
        var http = new HttpClient(handler);
        return new AnthropicClient(http, config);
    }

    private static LlmRequest SimpleRequest() => new()
    {
        Model = "claude-sonnet-4-20250514",
        Messages = [LlmMessage.FromText(LlmRole.User, "Hi")]
    };

    // ─── CompleteAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_ReturnsSuccessResponse()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, SuccessJson);
        using var client = CreateClient(handler);

        var response = await client.CompleteAsync(SimpleRequest());

        Assert.True(response.Success);
        Assert.Equal("Test response", response.Content);
        Assert.Equal("claude-sonnet-4-20250514", response.Model);
        Assert.Equal(5, response.InputTokens);
        Assert.Equal(3, response.OutputTokens);
        Assert.NotNull(response.Latency);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsError_OnHttpFailure()
    {
        using var handler = new FakeHandler(HttpStatusCode.Unauthorized,
            """{"type":"error","error":{"type":"authentication_error","message":"Invalid API key"}}""");
        using var client = CreateClient(handler);

        var response = await client.CompleteAsync(SimpleRequest());

        Assert.False(response.Success);
        Assert.Contains("401", response.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_SendsXApiKeyHeader()
    {
        HttpRequestMessage? captured = null;
        using var handler = new FakeHandler(req =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessJson, Encoding.UTF8, "application/json")
            });
        });

        using var client = CreateClient(handler,
            new AnthropicConfig { ApiKey = "sk-ant-my-key", Model = "claude-sonnet-4-20250514" });

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        // Anthropic uses x-api-key header, NOT Bearer auth
        Assert.Null(captured!.Headers.Authorization);
        Assert.True(captured.Headers.Contains("x-api-key"));
        Assert.Equal("sk-ant-my-key", captured.Headers.GetValues("x-api-key").First());
    }

    [Fact]
    public async Task CompleteAsync_SendsAnthropicVersionHeader()
    {
        HttpRequestMessage? captured = null;
        using var handler = new FakeHandler(req =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessJson, Encoding.UTF8, "application/json")
            });
        });

        using var client = CreateClient(handler);

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        Assert.True(captured!.Headers.Contains("anthropic-version"));
        Assert.Equal("2023-06-01", captured.Headers.GetValues("anthropic-version").First());
    }

    [Fact]
    public async Task CompleteAsync_OmitsApiKey_WhenNull()
    {
        HttpRequestMessage? captured = null;
        using var handler = new FakeHandler(req =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessJson, Encoding.UTF8, "application/json")
            });
        });

        using var client = CreateClient(handler,
            new AnthropicConfig { ApiKey = null, Model = "claude-sonnet-4-20250514" });

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        Assert.False(captured!.Headers.Contains("x-api-key"));
    }

    [Fact]
    public async Task CompleteAsync_PostsToMessagesEndpoint()
    {
        HttpRequestMessage? captured = null;
        using var handler = new FakeHandler(req =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessJson, Encoding.UTF8, "application/json")
            });
        });

        using var client = CreateClient(handler);

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        Assert.Equal("https://api.anthropic.com/v1/messages", captured!.RequestUri!.ToString());
    }

    // ─── StreamAsync ────────────────────────────────────────────────

    [Fact]
    public async Task StreamAsync_YieldsTextDeltas()
    {
        var sseBody = string.Join("\n",
            """event: message_start""",
            """data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","content":[],"model":"claude-sonnet-4-20250514","stop_reason":null,"usage":{"input_tokens":10,"output_tokens":0}}}""",
            "",
            """event: content_block_start""",
            """data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            "",
            """event: content_block_delta""",
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}""",
            "",
            """event: content_block_delta""",
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" world"}}""",
            "",
            """event: content_block_stop""",
            """data: {"type":"content_block_stop","index":0}""",
            "",
            """event: message_delta""",
            """data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":5}}""",
            "",
            """event: message_stop""",
            """data: {"type":"message_stop"}""",
            "");

        using var handler = new FakeHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sseBody, Encoding.UTF8, "text/event-stream")
            }));

        using var client = CreateClient(handler);

        var chunks = new List<string>();
        await foreach (var chunk in client.StreamAsync(SimpleRequest()))
            chunks.Add(chunk);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Hello", chunks[0]);
        Assert.Equal(" world", chunks[1]);
    }

    // ─── properties ─────────────────────────────────────────────────

    [Fact]
    public void ProviderName_ComesFromConfig()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        using var client = CreateClient(handler,
            new AnthropicConfig { ProviderName = "custom-anthropic", Model = "claude-sonnet-4-20250514" });

        Assert.Equal("custom-anthropic", client.ProviderName);
    }

    [Fact]
    public void ModelId_ComesFromConfig()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        using var client = CreateClient(handler,
            new AnthropicConfig { Model = "claude-opus-4-20250514" });

        Assert.Equal("claude-opus-4-20250514", client.ModelId);
    }

    [Fact]
    public void Capabilities_ReflectConfig()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        using var client = CreateClient(handler, new AnthropicConfig
        {
            Model = "claude-sonnet-4-20250514",
            Capabilities = new AnthropicCapabilitiesConfig
            {
                SupportsVision = true,
                SupportsToolCalling = true,
                SupportsJsonMode = true,
                SupportsStreaming = true,
                SupportsAudio = false,
                SupportsVideo = false,
                MaxContextTokens = 200_000,
                MaxOutputTokens = 8_192
            }
        });

        Assert.True(client.Capabilities.SupportsVision);
        Assert.True(client.Capabilities.SupportsToolCalling);
        Assert.Equal(200_000, client.Capabilities.MaxContextTokens);
        Assert.Equal(8_192, client.Capabilities.MaxOutputTokens);
    }
}