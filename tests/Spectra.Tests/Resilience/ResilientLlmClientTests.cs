using Spectra.Contracts.Providers;
using Spectra.Kernel.Resilience;
using Xunit;

namespace Spectra.Tests.Resilience;

public class ResilientLlmClientTests
{
    private static LlmResilienceOptions FastOptions(int maxRetries = 2) => new()
    {
        MaxRetries = maxRetries,
        BaseDelay = TimeSpan.FromMilliseconds(10),
        MaxDelay = TimeSpan.FromMilliseconds(100),
        Timeout = TimeSpan.FromSeconds(5),
        UseExponentialBackoff = false
    };

    [Fact]
    public async Task SuccessOnFirstAttempt_ReturnsResponse()
    {
        var inner = new FakeLlmClient(LlmResponse("ok"));
        var sut = new ResilientLlmClient(inner, FastOptions());

        var result = await sut.CompleteAsync(AnyRequest());

        Assert.True(result.Success);
        Assert.Equal("ok", result.Content);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task TransientFailureThenSuccess_Retries()
    {
        var inner = new FakeLlmClient(
            Contracts.Providers.LlmResponse.Error("HTTP 503: service unavailable"),
            LlmResponse("recovered"));

        var sut = new ResilientLlmClient(inner, FastOptions());

        var result = await sut.CompleteAsync(AnyRequest());

        Assert.True(result.Success);
        Assert.Equal("recovered", result.Content);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task AllAttemptsExhausted_ReturnsLastError()
    {
        var error = Contracts.Providers.LlmResponse.Error("HTTP 500: internal error");
        var inner = new FakeLlmClient(error, error, error);

        var sut = new ResilientLlmClient(inner, FastOptions(maxRetries: 2));

        var result = await sut.CompleteAsync(AnyRequest());

        Assert.False(result.Success);
        Assert.Equal(3, inner.CallCount);
    }

    [Fact]
    public async Task NonRetryableError_FailsFast()
    {
        var error = Contracts.Providers.LlmResponse.Error("HTTP 401: unauthorized");
        var inner = new FakeLlmClient(error);

        var sut = new ResilientLlmClient(inner, FastOptions());

        var result = await sut.CompleteAsync(AnyRequest());

        Assert.False(result.Success);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Timeout_TriggersRetry()
    {
        var options = FastOptions() with { Timeout = TimeSpan.FromMilliseconds(50) };

        var inner = new FakeLlmClient(
            delay: TimeSpan.FromSeconds(10),  // first call will timeout
            responses: [LlmResponse("ok")]);  // second call succeeds instantly

        // Override: first call hangs, second returns fast
        var hangThenSucceed = new TimeoutThenSucceedClient();
        var sut = new ResilientLlmClient(hangThenSucceed, options);

        var result = await sut.CompleteAsync(AnyRequest());

        Assert.True(result.Success);
        Assert.Equal("ok", result.Content);
        Assert.Equal(2, hangThenSucceed.CallCount);
    }

    [Fact]
    public async Task CancellationRespected_ThrowsOperationCanceled()
    {
        var inner = new FakeLlmClient(delay: TimeSpan.FromSeconds(30));
        var sut = new ResilientLlmClient(inner, FastOptions());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.CompleteAsync(AnyRequest(), cts.Token));
    }

    [Fact]
    public async Task HttpRequestException_Retries()
    {
        var inner = new ThrowThenSucceedClient(
            new HttpRequestException("connection reset"),
            LlmResponse("ok"));

        var sut = new ResilientLlmClient(inner, FastOptions());

        var result = await sut.CompleteAsync(AnyRequest());

        Assert.True(result.Success);
        Assert.Equal("ok", result.Content);
    }

    [Fact]
    public async Task ExponentialBackoff_DelaysIncrease()
    {
        var options = new LlmResilienceOptions
        {
            MaxRetries = 2,
            BaseDelay = TimeSpan.FromMilliseconds(100),
            MaxDelay = TimeSpan.FromSeconds(5),
            Timeout = TimeSpan.FromSeconds(5),
            UseExponentialBackoff = true
        };

        var error = Contracts.Providers.LlmResponse.Error("HTTP 429: rate limited");
        var inner = new FakeLlmClient(error, error, LlmResponse("ok"));
        var sut = new ResilientLlmClient(inner, options);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await sut.CompleteAsync(AnyRequest());
        sw.Stop();

        Assert.True(result.Success);
        // With exponential backoff: ~100ms + ~200ms = ~300ms minimum
        Assert.True(sw.ElapsedMilliseconds >= 200, $"Expected >= 200ms, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void ProxiesProviderProperties()
    {
        var inner = new FakeLlmClient();
        var sut = new ResilientLlmClient(inner);

        Assert.Equal(inner.ProviderName, sut.ProviderName);
        Assert.Equal(inner.ModelId, sut.ModelId);
        Assert.Same(inner.Capabilities, sut.Capabilities);
    }

    // --- Helpers ---

    private static LlmRequest AnyRequest() => new()
    {
        Model = "test-model",
        Messages = [LlmMessage.FromText(LlmRole.User, "hello")]
    };

    private static LlmResponse LlmResponse(string content) => new()
    {
        Content = content,
        Success = true
    };

    private class FakeLlmClient : ILlmClient
    {
        private readonly Queue<Func<CancellationToken, Task<LlmResponse>>> _handlers = new();
        public int CallCount { get; private set; }

        public string ProviderName => "fake";
        public string ModelId => "fake-model";

        public ModelCapabilities Capabilities { get; } = new();

        public FakeLlmClient(params LlmResponse[] responses)
        {
            foreach (var r in responses)
                _handlers.Enqueue(_ => Task.FromResult(r));
        }

        public FakeLlmClient(TimeSpan delay, LlmResponse[]? responses = null)
        {
            _handlers.Enqueue(async ct =>
            {
                await Task.Delay(delay, ct);
                return Contracts.Providers.LlmResponse.Error("delayed");
            });

            if (responses is not null)
                foreach (var r in responses)
                    _handlers.Enqueue(_ => Task.FromResult(r));
        }

        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_handlers.Count > 0)
                return await _handlers.Dequeue()(cancellationToken);

            return Contracts.Providers.LlmResponse.Error("no more responses");
        }
    }

    private class TimeoutThenSucceedClient : ILlmClient
    {
        public int CallCount { get; private set; }
        public string ProviderName => "fake";
        public string ModelId => "fake-model";
        public ModelCapabilities Capabilities => new();

        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 1)
            {
                // Simulate a long-running call that will be cancelled by timeout
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return Contracts.Providers.LlmResponse.Error("should not reach here");
            }

            return new LlmResponse { Content = "ok", Success = true };
        }
    }

    private class ThrowThenSucceedClient : ILlmClient
    {
        private readonly Exception _exception;
        private readonly LlmResponse _successResponse;
        private int _callCount;

        public int CallCount => _callCount;
        public string ProviderName => "fake";
        public string ModelId => "fake-model";
        public ModelCapabilities Capabilities => new();

        public ThrowThenSucceedClient(Exception exception, LlmResponse successResponse)
        {
            _exception = exception;
            _successResponse = successResponse;
        }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount == 1)
                throw _exception;

            return Task.FromResult(_successResponse);
        }
    }
}