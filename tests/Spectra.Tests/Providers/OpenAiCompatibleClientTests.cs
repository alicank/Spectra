using System.Net;
using System.Text;
using Spectra.Contracts.Providers;
using Spectra.Extensions.Providers.OpenAiCompatible;
using Xunit;

namespace Spectra.Tests.Providers;

public class OpenAiCompatibleClientTests
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

        public HttpRequestMessage? LastRequest { get; private set; }
    }

    private static readonly string SuccessJson = """
        {
            "id": "chatcmpl-test",
            "model": "gpt-4o",
            "choices": [{
                "index": 0,
                "message": { "role": "assistant", "content": "Test response" },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 3, "total_tokens": 8 }
        }
        """;

    private static OpenAiCompatibleClient CreateClient(
        DelegatingHandler handler, OpenAiConfig? config = null)
    {
        config ??= new OpenAiConfig { ApiKey = "sk-test", Model = "gpt-4o" };
        var http = new HttpClient(handler);
        return new OpenAiCompatibleClient(http, config);
    }

    private static LlmRequest SimpleRequest() => new()
    {
        Model = "gpt-4o",
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
        Assert.Equal("gpt-4o", response.Model);
        Assert.Equal(5, response.InputTokens);
        Assert.Equal(3, response.OutputTokens);
        Assert.NotNull(response.Latency);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsError_OnHttpFailure()
    {
        using var handler = new FakeHandler(HttpStatusCode.Unauthorized,
            """{"error":{"message":"Invalid API key"}}""");
        using var client = CreateClient(handler);

        var response = await client.CompleteAsync(SimpleRequest());

        Assert.False(response.Success);
        Assert.Contains("401", response.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_SendsBearerToken()
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
            new OpenAiConfig { ApiKey = "sk-my-key", Model = "gpt-4o" });

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("sk-my-key", captured.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task CompleteAsync_OmitsAuth_WhenApiKeyIsNull()
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
            new OpenAiConfig { ApiKey = null, Model = "llama3", BaseUrl = "http://localhost:11434/v1" });

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        Assert.Null(captured!.Headers.Authorization);
    }

    [Fact]
    public async Task CompleteAsync_IncludesApiVersion_InQueryString()
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
            new OpenAiConfig
            {
                ApiKey = "key",
                Model = "gpt-4o",
                BaseUrl = "https://my-resource.openai.azure.com/openai/deployments/gpt-4o",
                ApiVersion = "2024-02-15-preview"
            });

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        Assert.Contains("api-version=2024-02-15-preview", captured!.RequestUri!.Query);
    }

    [Fact]
    public async Task CompleteAsync_SendsOrganizationHeader()
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
            new OpenAiConfig { ApiKey = "key", Model = "gpt-4o", Organization = "org-abc" });

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        Assert.True(captured!.Headers.Contains("OpenAI-Organization"));
        Assert.Equal("org-abc", captured.Headers.GetValues("OpenAI-Organization").First());
    }

    // ─── StreamAsync ────────────────────────────────────────────────

    [Fact]
    public async Task StreamAsync_YieldsTextDeltas()
    {
        var sseBody = """
            data: {"choices":[{"index":0,"delta":{"role":"assistant"},"finish_reason":null}]}
            data: {"choices":[{"index":0,"delta":{"content":"Hello"},"finish_reason":null}]}
            data: {"choices":[{"index":0,"delta":{"content":" world"},"finish_reason":null}]}
            data: {"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}
            data: [DONE]
            """;

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
            new OpenAiConfig { ProviderName = "deepseek", Model = "deepseek-chat" });

        Assert.Equal("deepseek", client.ProviderName);
    }

    [Fact]
    public void ModelId_ComesFromConfig()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        using var client = CreateClient(handler,
            new OpenAiConfig { Model = "gpt-4o-mini" });

        Assert.Equal("gpt-4o-mini", client.ModelId);
    }

    [Fact]
    public void Capabilities_ReflectConfig()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        using var client = CreateClient(handler, new OpenAiConfig
        {
            Model = "gpt-4o",
            Capabilities = new ModelCapabilitiesConfig
            {
                SupportsVision = true,
                SupportsToolCalling = true,
                SupportsJsonMode = true,
                SupportsStreaming = true,
                SupportsAudio = false,
                SupportsVideo = false,
                MaxContextTokens = 128_000,
                MaxOutputTokens = 4096
            }
        });

        Assert.True(client.Capabilities.SupportsVision);
        Assert.True(client.Capabilities.SupportsToolCalling);
        Assert.Equal(128_000, client.Capabilities.MaxContextTokens);
        Assert.Equal(4096, client.Capabilities.MaxOutputTokens);
    }
}