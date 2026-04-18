using Spectra.Contracts.Caching;
using Spectra.Contracts.Providers;
using Spectra.Kernel.Caching;
using Xunit;

namespace Spectra.Tests.Caching;

public class CachingLlmClientTests
{
    private readonly InMemoryCacheStore _cache = new();
    private readonly FakeLlmClient _inner = new();

    private CachingLlmClient CreateSut(LlmCacheOptions? options = null)
        => new(_inner, _cache, options);

    [Fact]
    public async Task CompleteAsync_CachesMissAndReturnsResponse()
    {
        var sut = CreateSut();
        var request = MakeRequest("Hello");

        var result = await sut.CompleteAsync(request);

        Assert.Equal("fake-response", result.Content);
        Assert.Equal(1, _inner.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsCachedResponseOnHit()
    {
        var sut = CreateSut();
        var request = MakeRequest("Hello");

        await sut.CompleteAsync(request);
        var result = await sut.CompleteAsync(request);

        Assert.Equal("fake-response", result.Content);
        Assert.Equal(1, _inner.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_DifferentRequestsGetDifferentCacheEntries()
    {
        var sut = CreateSut();

        await sut.CompleteAsync(MakeRequest("Hello"));
        await sut.CompleteAsync(MakeRequest("World"));

        Assert.Equal(2, _inner.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_SkipsCacheWhenResponseHasToolCalls()
    {
        _inner.ResponseOverride = new LlmResponse
        {
            Content = "tool-response",
            ToolCalls = [new ToolCall { Id = "1", Name = "test" }]
        };
        var sut = CreateSut();
        var request = MakeRequest("Hello");

        await sut.CompleteAsync(request);
        await sut.CompleteAsync(request);

        Assert.Equal(2, _inner.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_CachesToolCallResponseWhenOptionDisabled()
    {
        _inner.ResponseOverride = new LlmResponse
        {
            Content = "tool-response",
            ToolCalls = [new ToolCall { Id = "1", Name = "test" }]
        };
        var sut = CreateSut(new LlmCacheOptions { SkipWhenToolCalls = false });
        var request = MakeRequest("Hello");

        await sut.CompleteAsync(request);
        await sut.CompleteAsync(request);

        Assert.Equal(1, _inner.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_SkipsCacheWhenRequestHasMedia()
    {
        var sut = CreateSut();
        var request = MakeRequest("Describe this", hasMedia: true);

        await sut.CompleteAsync(request);
        await sut.CompleteAsync(request);

        Assert.Equal(2, _inner.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_DoesNotCacheErrorResponses()
    {
        _inner.ResponseOverride = LlmResponse.Error("something broke");
        var sut = CreateSut();
        var request = MakeRequest("Hello");

        await sut.CompleteAsync(request);

        _inner.ResponseOverride = null;
        var result = await sut.CompleteAsync(request);

        Assert.Equal(2, _inner.CallCount);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task CompleteAsync_RespectsTtlExpiry()
    {
        var shortTtl = new LlmCacheOptions { DefaultTtl = TimeSpan.FromMilliseconds(50) };
        var sut = CreateSut(shortTtl);
        var request = MakeRequest("Hello");

        await sut.CompleteAsync(request);
        await Task.Delay(100);
        await sut.CompleteAsync(request);

        Assert.Equal(2, _inner.CallCount);
    }

    [Fact]
    public Task CompleteAsync_DelegatesProviderProperties()
    {
        var sut = CreateSut();

        Assert.Equal("fake", sut.ProviderName);
        Assert.Equal("fake-model", sut.ModelId);
        Assert.True(sut.Capabilities.SupportsJsonMode);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CompleteAsync_BypassesCacheWhenEnabledIsFalse()
    {
        var sut = CreateSut(new LlmCacheOptions { Enabled = false });
        var request = MakeRequest("Hello");

        await sut.CompleteAsync(request);
        await sut.CompleteAsync(request);

        Assert.Equal(2, _inner.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_BypassesCacheWhenRequestSkipCacheIsTrue()
    {
        var sut = CreateSut();
        var request = new LlmRequest
        {
            Model = "fake-model",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hello")],
            SkipCache = true
        };

        await sut.CompleteAsync(request);
        await sut.CompleteAsync(request);

        Assert.Equal(2, _inner.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_EnabledCanBeToggledAtRuntime()
    {
        var options = new LlmCacheOptions { Enabled = true };
        var sut = new CachingLlmClient(_inner, _cache, options);
        var request = MakeRequest("Hello");

        await sut.CompleteAsync(request);
        Assert.Equal(1, _inner.CallCount);

        // cached hit
        await sut.CompleteAsync(request);
        Assert.Equal(1, _inner.CallCount);

        // flip off at runtime
        options.Enabled = false;
        await sut.CompleteAsync(request);
        Assert.Equal(2, _inner.CallCount);

        // flip back on — cache entry still exists
        options.Enabled = true;
        await sut.CompleteAsync(request);
        Assert.Equal(2, _inner.CallCount);
    }

    private static LlmRequest MakeRequest(string content, bool hasMedia = false)
    {
        var messages = new List<LlmMessage>();

        if (hasMedia)
        {
            messages.Add(new LlmMessage
            {
                Role = LlmRole.User,
                Content = content,
                ContentParts =
                [
                    LlmMessage.MediaContent.FromText(content),
                    LlmMessage.MediaContent.FromImage("base64data")
                ]
            });
        }
        else
        {
            messages.Add(LlmMessage.FromText(LlmRole.User, content));
        }

        return new LlmRequest { Model = "fake-model", Messages = messages };
    }

    private sealed class FakeLlmClient : ILlmClient
    {
        public int CallCount { get; private set; }
        public LlmResponse? ResponseOverride { get; set; }

        public string ProviderName => "fake";
        public string ModelId => "fake-model";
        public ModelCapabilities Capabilities => new() { SupportsJsonMode = true };

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var response = ResponseOverride ?? new LlmResponse { Content = "fake-response" };
            return Task.FromResult(response);
        }
    }
}