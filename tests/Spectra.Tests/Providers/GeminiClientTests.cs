using System.Net;
using System.Text;
using Spectra.Contracts.Providers;
using Spectra.Extensions.Providers.Gemini;
using Xunit;

namespace Spectra.Tests.Providers;

public class GeminiClientTests
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
            "candidates": [{
                "content": {
                    "parts": [{ "text": "Test response" }],
                    "role": "model"
                },
                "finishReason": "STOP"
            }],
            "usageMetadata": {
                "promptTokenCount": 5,
                "candidatesTokenCount": 3,
                "totalTokenCount": 8
            }
        }
        """;

    private static GeminiClient CreateClient(
        DelegatingHandler handler, GeminiConfig? config = null)
    {
        config ??= new GeminiConfig { ApiKey = "test-key", Model = "gemini-2.0-flash" };
        var http = new HttpClient(handler);
        return new GeminiClient(http, config);
    }

    private static LlmRequest SimpleRequest() => new()
    {
        Model = "gemini-2.0-flash",
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
        Assert.Equal(5, response.InputTokens);
        Assert.Equal(3, response.OutputTokens);
        Assert.NotNull(response.Latency);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsError_OnHttpFailure()
    {
        using var handler = new FakeHandler(HttpStatusCode.Unauthorized,
            """{"error":{"code":401,"message":"API key not valid."}}""");
        using var client = CreateClient(handler);

        var response = await client.CompleteAsync(SimpleRequest());

        Assert.False(response.Success);
        Assert.Contains("401", response.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_SendsApiKeyAsQueryParam()
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
            new GeminiConfig { ApiKey = "my-gemini-key", Model = "gemini-2.0-flash" });

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        // Gemini uses query param, NOT Bearer auth or custom header
        Assert.Null(captured!.Headers.Authorization);
        Assert.Contains("key=my-gemini-key", captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task CompleteAsync_PostsToGenerateContentEndpoint()
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
        var url = captured!.RequestUri!.ToString();
        Assert.Contains("/models/gemini-2.0-flash:generateContent", url);
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
            new GeminiConfig { ApiKey = null, Model = "gemini-2.0-flash" });

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        Assert.DoesNotContain("key=", captured!.RequestUri!.ToString());
    }

    // ─── StreamAsync ────────────────────────────────────────────────

    [Fact]
    public async Task StreamAsync_YieldsTextDeltas()
    {
        var sseBody = string.Join("\n",
            """data: {"candidates":[{"content":{"parts":[{"text":"Hello"}],"role":"model"}}]}""",
            "",
            """data: {"candidates":[{"content":{"parts":[{"text":" world"}],"role":"model"}}]}""",
            "",
            """data: [DONE]""",
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

    [Fact]
    public async Task StreamAsync_PostsToStreamEndpoint()
    {
        HttpRequestMessage? captured = null;
        using var handler = new FakeHandler(req =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: [DONE]\n", Encoding.UTF8, "text/event-stream")
            });
        });

        using var client = CreateClient(handler);

        await foreach (var _ in client.StreamAsync(SimpleRequest())) { }

        Assert.NotNull(captured);
        var url = captured!.RequestUri!.ToString();
        Assert.Contains("/models/gemini-2.0-flash:streamGenerateContent", url);
        Assert.Contains("alt=sse", url);
    }

    // ─── properties ─────────────────────────────────────────────────

    [Fact]
    public void ProviderName_ComesFromConfig()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        using var client = CreateClient(handler,
            new GeminiConfig { ProviderName = "custom-gemini", Model = "gemini-2.0-flash" });

        Assert.Equal("custom-gemini", client.ProviderName);
    }

    [Fact]
    public void ModelId_ComesFromConfig()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        using var client = CreateClient(handler,
            new GeminiConfig { Model = "gemini-1.5-pro" });

        Assert.Equal("gemini-1.5-pro", client.ModelId);
    }

    [Fact]
    public void Capabilities_ReflectConfig()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        using var client = CreateClient(handler, new GeminiConfig
        {
            Model = "gemini-2.0-flash",
            Capabilities = new GeminiCapabilitiesConfig
            {
                SupportsVision = true,
                SupportsToolCalling = true,
                SupportsJsonMode = true,
                SupportsStreaming = true,
                SupportsAudio = true,
                SupportsVideo = true,
                MaxContextTokens = 1_048_576,
                MaxOutputTokens = 8_192
            }
        });

        Assert.True(client.Capabilities.SupportsVision);
        Assert.True(client.Capabilities.SupportsToolCalling);
        Assert.True(client.Capabilities.SupportsAudio);
        Assert.True(client.Capabilities.SupportsVideo);
        Assert.Equal(1_048_576, client.Capabilities.MaxContextTokens);
        Assert.Equal(8_192, client.Capabilities.MaxOutputTokens);
    }
}