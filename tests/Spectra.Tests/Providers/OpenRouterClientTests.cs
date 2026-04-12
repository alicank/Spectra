using System.Net;
using System.Text;
using Spectra.Contracts.Providers;
using Spectra.Extensions.Providers.OpenRouter;
using Xunit;

namespace Spectra.Tests.Providers;

public class OpenRouterClientTests
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
            "id": "gen-test123",
            "model": "openai/gpt-4o",
            "choices": [{
                "index": 0,
                "message": { "role": "assistant", "content": "Test response" },
                "finish_reason": "stop"
            }],
            "usage": { "prompt_tokens": 5, "completion_tokens": 3, "total_tokens": 8 }
        }
        """;

    private static OpenRouterClient CreateClient(
        DelegatingHandler handler, OpenRouterConfig? config = null)
    {
        config ??= new OpenRouterConfig { ApiKey = "sk-or-test", Model = "openai/gpt-4o" };
        var http = new HttpClient(handler);
        return new OpenRouterClient(http, config);
    }

    private static LlmRequest SimpleRequest() => new()
    {
        Model = "openai/gpt-4o",
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
        Assert.Equal("openai/gpt-4o", response.Model);
        Assert.Equal(5, response.InputTokens);
        Assert.Equal(3, response.OutputTokens);
        Assert.NotNull(response.Latency);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsError_OnHttpFailure()
    {
        using var handler = new FakeHandler(HttpStatusCode.Unauthorized,
            """{"error":{"message":"Invalid API key","code":401}}""");
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
            new OpenRouterConfig { ApiKey = "sk-or-my-key", Model = "openai/gpt-4o" });

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("sk-or-my-key", captured.Headers.Authorization.Parameter);
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
            new OpenRouterConfig { ApiKey = null, Model = "openai/gpt-4o" });

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        Assert.Null(captured!.Headers.Authorization);
    }

    [Fact]
    public async Task CompleteAsync_SendsRefererHeader_WhenSiteUrlIsSet()
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
            new OpenRouterConfig
            {
                ApiKey = "sk-or-test",
                Model = "openai/gpt-4o",
                SiteUrl = "https://myapp.com"
            });

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        Assert.True(captured!.Headers.Contains("HTTP-Referer"));
        Assert.Equal("https://myapp.com", captured.Headers.GetValues("HTTP-Referer").First());
    }

    [Fact]
    public async Task CompleteAsync_SendsTitleHeader_WhenSiteNameIsSet()
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
            new OpenRouterConfig
            {
                ApiKey = "sk-or-test",
                Model = "openai/gpt-4o",
                SiteName = "MyApp"
            });

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        Assert.True(captured!.Headers.Contains("X-Title"));
        Assert.Equal("MyApp", captured.Headers.GetValues("X-Title").First());
    }

    [Fact]
    public async Task CompleteAsync_OmitsRefererAndTitle_WhenNotSet()
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
            new OpenRouterConfig { ApiKey = "sk-or-test", Model = "openai/gpt-4o" });

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        Assert.False(captured!.Headers.Contains("HTTP-Referer"));
        Assert.False(captured.Headers.Contains("X-Title"));
    }

    [Fact]
    public async Task CompleteAsync_PostsToChatCompletionsEndpoint()
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
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions",
            captured!.RequestUri!.ToString());
    }

    [Fact]
    public async Task CompleteAsync_UsesCustomBaseUrl()
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
            new OpenRouterConfig
            {
                ApiKey = "sk-or-test",
                Model = "openai/gpt-4o",
                BaseUrl = "https://proxy.example.com/v1"
            });

        await client.CompleteAsync(SimpleRequest());

        Assert.NotNull(captured);
        Assert.Equal("https://proxy.example.com/v1/chat/completions",
            captured!.RequestUri!.ToString());
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

    [Fact]
    public async Task StreamAsync_SendsCustomHeaders()
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

        using var client = CreateClient(handler,
            new OpenRouterConfig
            {
                ApiKey = "sk-or-test",
                Model = "openai/gpt-4o",
                SiteUrl = "https://myapp.com",
                SiteName = "MyApp"
            });

        await foreach (var _ in client.StreamAsync(SimpleRequest())) { }

        Assert.NotNull(captured);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.True(captured.Headers.Contains("HTTP-Referer"));
        Assert.True(captured.Headers.Contains("X-Title"));
    }

    // ─── properties ─────────────────────────────────────────────────

    [Fact]
    public void ProviderName_ComesFromConfig()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        using var client = CreateClient(handler,
            new OpenRouterConfig { ProviderName = "custom-router", Model = "openai/gpt-4o" });

        Assert.Equal("custom-router", client.ProviderName);
    }

    [Fact]
    public void ModelId_ComesFromConfig()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        using var client = CreateClient(handler,
            new OpenRouterConfig { Model = "anthropic/claude-sonnet-4" });

        Assert.Equal("anthropic/claude-sonnet-4", client.ModelId);
    }

    [Fact]
    public void ModelId_UsesOverride_WhenProvided()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        var config = new OpenRouterConfig { Model = "openai/gpt-4o" };
        var http = new HttpClient(handler);
        using var client = new OpenRouterClient(http, config, "google/gemini-2.0-flash");

        Assert.Equal("google/gemini-2.0-flash", client.ModelId);
    }

    [Fact]
    public void Capabilities_ReflectConfig()
    {
        using var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        using var client = CreateClient(handler, new OpenRouterConfig
        {
            Model = "openai/gpt-4o",
            Capabilities = new OpenRouterCapabilitiesConfig
            {
                SupportsVision = true,
                SupportsToolCalling = true,
                SupportsJsonMode = true,
                SupportsStreaming = true,
                SupportsAudio = false,
                SupportsVideo = false,
                MaxContextTokens = 128_000,
                MaxOutputTokens = 4_096
            }
        });

        Assert.True(client.Capabilities.SupportsVision);
        Assert.True(client.Capabilities.SupportsToolCalling);
        Assert.True(client.Capabilities.SupportsJsonMode);
        Assert.True(client.Capabilities.SupportsStreaming);
        Assert.False(client.Capabilities.SupportsAudio);
        Assert.False(client.Capabilities.SupportsVideo);
        Assert.Equal(128_000, client.Capabilities.MaxContextTokens);
        Assert.Equal(4_096, client.Capabilities.MaxOutputTokens);
    }
}