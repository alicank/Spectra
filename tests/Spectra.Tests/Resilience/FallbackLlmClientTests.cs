using Spectra.Contracts.Events;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Providers.Fallback;
using Spectra.Kernel.Resilience;
using Xunit;

namespace Spectra.Tests.Resilience;

public class FallbackLlmClientTests
{
    // --- Strategy: Failover ---

    [Fact]
    public async Task Failover_PrimarySucceeds_ReturnsPrimaryResponse()
    {
        var primary = new FakeFallbackClient("openai", "gpt-4o", Ok("primary response"));
        var secondary = new FakeFallbackClient("anthropic", "claude-sonnet", Ok("secondary response"));

        var sut = CreateClient(FallbackStrategy.Failover, primary, secondary);

        var result = await sut.CompleteAsync(AnyRequest());

        Assert.True(result.Success);
        Assert.Contains("primary response", result.Content);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(0, secondary.CallCount);
    }

    [Fact]
    public async Task Failover_PrimaryFails_FallsToSecondary()
    {
        var primary = new FakeFallbackClient("openai", "gpt-4o",
            LlmResponse.Error("HTTP 503: service unavailable"));
        var secondary = new FakeFallbackClient("anthropic", "claude-sonnet", Ok("fallback response"));

        var sut = CreateClient(FallbackStrategy.Failover, primary, secondary);

        var result = await sut.CompleteAsync(AnyRequest());

        Assert.True(result.Success);
        Assert.Contains("fallback response", result.Content);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, secondary.CallCount);
    }

    [Fact]
    public async Task Failover_AllFail_ReturnsError()
    {
        var primary = new FakeFallbackClient("openai", "gpt-4o",
            LlmResponse.Error("HTTP 503: down"));
        var secondary = new FakeFallbackClient("anthropic", "claude-sonnet",
            LlmResponse.Error("HTTP 500: error"));

        var sut = CreateClient(FallbackStrategy.Failover, primary, secondary);

        var result = await sut.CompleteAsync(AnyRequest());

        Assert.False(result.Success);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, secondary.CallCount);
    }

    [Fact]
    public async Task Failover_PrimaryThrows_FallsToSecondary()
    {
        var primary = new FakeFallbackClient("openai", "gpt-4o",
            new HttpRequestException("connection refused"));
        var secondary = new FakeFallbackClient("anthropic", "claude-sonnet", Ok("recovered"));

        var sut = CreateClient(FallbackStrategy.Failover, primary, secondary);

        var result = await sut.CompleteAsync(AnyRequest());

        Assert.True(result.Success);
        Assert.Contains("recovered", result.Content);
    }

    // --- Strategy: RoundRobin ---

    [Fact]
    public async Task RoundRobin_DistributesAcrossProviders()
    {
        var a = new FakeFallbackClient("openai", "gpt-4o", Ok("a"));
        var b = new FakeFallbackClient("anthropic", "claude-sonnet", Ok("b"));

        var sut = CreateClient(FallbackStrategy.RoundRobin, a, b);

        await sut.CompleteAsync(AnyRequest());
        await sut.CompleteAsync(AnyRequest());

        // Both should have been called at least once
        Assert.True(a.CallCount >= 1);
        Assert.True(b.CallCount >= 1);
    }

    // --- Strategy: Weighted ---

    [Fact]
    public async Task Weighted_SelectsBasedOnWeight()
    {
        var heavy = new FakeFallbackClient("openai", "gpt-4o", Ok("heavy"));
        var light = new FakeFallbackClient("anthropic", "claude-sonnet", Ok("light"));

        var policy = new FallbackPolicy
        {
            Name = "weighted-test",
            Strategy = FallbackStrategy.Weighted,
            Entries =
            [
                new FallbackProviderEntry { Provider = "openai", Model = "gpt-4o", Weight = 90 },
                new FallbackProviderEntry { Provider = "anthropic", Model = "claude-sonnet", Weight = 10 }
            ]
        };

        var entries = new List<FallbackClientEntry>
        {
            new() { Client = heavy, Entry = policy.Entries[0] },
            new() { Client = light, Entry = policy.Entries[1] }
        };

        var sut = new FallbackLlmClient(policy, entries);

        // Run enough times that both should be hit at least once statistically
        for (var i = 0; i < 100; i++)
            await sut.CompleteAsync(AnyRequest());

        Assert.True(heavy.CallCount > light.CallCount,
            $"Heavy ({heavy.CallCount}) should be called more than light ({light.CallCount})");
    }

    // --- Strategy: Split ---

    [Fact]
    public async Task Split_DeterministicBucketing()
    {
        var a = new FakeFallbackClient("openai", "gpt-4o", Ok("a"));
        var b = new FakeFallbackClient("anthropic", "claude-sonnet", Ok("b"));

        var policy = new FallbackPolicy
        {
            Name = "split-test",
            Strategy = FallbackStrategy.Split,
            Entries =
            [
                new FallbackProviderEntry { Provider = "openai", Model = "gpt-4o", Weight = 50 },
                new FallbackProviderEntry { Provider = "anthropic", Model = "claude-sonnet", Weight = 50 }
            ]
        };

        var entries = new List<FallbackClientEntry>
        {
            new() { Client = a, Entry = policy.Entries[0] },
            new() { Client = b, Entry = policy.Entries[1] }
        };

        var sut = new FallbackLlmClient(policy, entries);

        for (var i = 0; i < 100; i++)
            await sut.CompleteAsync(AnyRequest());

        // With 50/50 split, both should be called approximately equally
        Assert.True(a.CallCount >= 40 && a.CallCount <= 60,
            $"Expected ~50 calls for A, got {a.CallCount}");
        Assert.True(b.CallCount >= 40 && b.CallCount <= 60,
            $"Expected ~50 calls for B, got {b.CallCount}");
    }

    // --- Quality Gate ---

    [Fact]
    public async Task QualityGate_RejectsShortResponse_FallsToNext()
    {
        var primary = new FakeFallbackClient("openai", "gpt-4o", Ok("ok"));  // too short
        var secondary = new FakeFallbackClient("anthropic", "claude-sonnet",
            Ok("This is a sufficiently long response that passes the quality gate."));

        var gate = new MinLengthQualityGate(minimumLength: 20);

        var policy = new FallbackPolicy
        {
            Name = "gated",
            Strategy = FallbackStrategy.Failover,
            Entries =
            [
                new FallbackProviderEntry { Provider = "openai", Model = "gpt-4o" },
                new FallbackProviderEntry { Provider = "anthropic", Model = "claude-sonnet" }
            ],
            DefaultQualityGate = gate
        };

        var entries = new List<FallbackClientEntry>
        {
            new() { Client = primary, Entry = policy.Entries[0] },
            new() { Client = secondary, Entry = policy.Entries[1] }
        };

        var sut = new FallbackLlmClient(policy, entries);
        var result = await sut.CompleteAsync(AnyRequest());

        Assert.True(result.Success);
        Assert.Contains("sufficiently long", result.Content);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, secondary.CallCount);
    }

    [Fact]
    public async Task QualityGate_PerEntryOverride()
    {
        var primary = new FakeFallbackClient("openai", "gpt-4o", Ok("x"));
        var secondary = new FakeFallbackClient("anthropic", "claude-sonnet", Ok("y"));

        // Per-entry gate on primary only
        var strictGate = new MinLengthQualityGate(minimumLength: 50);

        var policy = new FallbackPolicy
        {
            Name = "per-entry-gate",
            Strategy = FallbackStrategy.Failover,
            Entries =
            [
                new FallbackProviderEntry { Provider = "openai", Model = "gpt-4o", QualityGate = strictGate },
                new FallbackProviderEntry { Provider = "anthropic", Model = "claude-sonnet" }
            ]
        };

        var entries = new List<FallbackClientEntry>
        {
            new() { Client = primary, Entry = policy.Entries[0] },
            new() { Client = secondary, Entry = policy.Entries[1] }
        };

        var sut = new FallbackLlmClient(policy, entries);
        var result = await sut.CompleteAsync(AnyRequest());

        Assert.True(result.Success);
        // Primary rejected by its gate, secondary has no gate so "y" passes
        Assert.Contains("y", result.Content);
    }

    // --- Events ---

    [Fact]
    public async Task EmitsFallbackTriggeredEvent()
    {
        var sink = new RecordingEventSink();
        var primary = new FakeFallbackClient("openai", "gpt-4o",
            LlmResponse.Error("HTTP 503: down"));
        var secondary = new FakeFallbackClient("anthropic", "claude-sonnet", Ok("ok"));

        var sut = CreateClient(FallbackStrategy.Failover, primary, secondary, sink);

        await sut.CompleteAsync(AnyRequest());

        var triggered = sink.Events.OfType<FallbackTriggeredEvent>().ToList();
        Assert.Single(triggered);
        Assert.Equal("openai", triggered[0].FailedProvider);
        Assert.Equal("anthropic", triggered[0].NextProvider);
    }

    [Fact]
    public async Task EmitsFallbackExhaustedEvent()
    {
        var sink = new RecordingEventSink();
        var primary = new FakeFallbackClient("openai", "gpt-4o",
            LlmResponse.Error("HTTP 503: down"));
        var secondary = new FakeFallbackClient("anthropic", "claude-sonnet",
            LlmResponse.Error("HTTP 500: error"));

        var sut = CreateClient(FallbackStrategy.Failover, primary, secondary, sink);

        await sut.CompleteAsync(AnyRequest());

        var exhausted = sink.Events.OfType<FallbackExhaustedEvent>().ToList();
        Assert.Single(exhausted);
        Assert.Equal(2, exhausted[0].TotalAttempts);
    }

    // --- CompositeQualityGate ---

    [Fact]
    public void CompositeQualityGate_AllPass()
    {
        var gate = new CompositeQualityGate(
            new MinLengthQualityGate(5),
            new MinLengthQualityGate(3));

        var result = gate.Evaluate(Ok("hello world"));
        Assert.True(result.Passed);
    }

    [Fact]
    public void CompositeQualityGate_OneFails()
    {
        var gate = new CompositeQualityGate(
            new MinLengthQualityGate(5),
            new MinLengthQualityGate(100));

        var result = gate.Evaluate(Ok("hello world"));
        Assert.False(result.Passed);
    }

    // --- Cancellation ---

    [Fact]
    public async Task CancellationRespected()
    {
        var primary = new FakeFallbackClient("openai", "gpt-4o",
            delay: TimeSpan.FromSeconds(30));

        var sut = CreateClient(FallbackStrategy.Failover, primary);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.CompleteAsync(AnyRequest(), cts.Token));
    }

    // --- Helpers ---

    private static LlmRequest AnyRequest() => new()
    {
        Model = "test-model",
        Messages = [LlmMessage.FromText(LlmRole.User, "hello")]
    };

    private static LlmResponse Ok(string content) => new()
    {
        Content = content,
        Success = true
    };

    private static FallbackLlmClient CreateClient(
        FallbackStrategy strategy,
        params FakeFallbackClient[] clients)
    {
        return CreateClient(strategy, clients, null);
    }

    private static FallbackLlmClient CreateClient(
        FallbackStrategy strategy,
        FakeFallbackClient[] clients,
        IEventSink? sink)
    {
        var entries = clients.Select(c => new FallbackClientEntry
        {
            Client = c,
            Entry = new FallbackProviderEntry { Provider = c.ProviderName, Model = c.ModelId }
        }).ToList();

        var policy = new FallbackPolicy
        {
            Name = "test-policy",
            Strategy = strategy,
            Entries = entries.Select(e => e.Entry).ToList()
        };

        return new FallbackLlmClient(policy, entries, sink, "run-1", "wf-1");
    }

    private static FallbackLlmClient CreateClient(
        FallbackStrategy strategy,
        FakeFallbackClient primary,
        FakeFallbackClient secondary,
        IEventSink? sink = null)
    {
        return CreateClient(strategy, [primary, secondary], sink);
    }

    private static FallbackLlmClient CreateClient(
        FallbackStrategy strategy,
        FakeFallbackClient primary)
    {
        return CreateClient(strategy, [primary], null);
    }

    private class FakeFallbackClient : ILlmClient
    {
        private readonly Func<CancellationToken, Task<LlmResponse>> _handler;
        public int CallCount { get; private set; }
        public string ProviderName { get; }
        public string ModelId { get; }
        public ModelCapabilities Capabilities { get; } = new();

        public FakeFallbackClient(string provider, string model, LlmResponse response)
        {
            ProviderName = provider;
            ModelId = model;
            _handler = _ => Task.FromResult(response);
        }

        public FakeFallbackClient(string provider, string model, Exception exception)
        {
            ProviderName = provider;
            ModelId = model;
            _handler = _ => throw exception;
        }

        public FakeFallbackClient(string provider, string model, TimeSpan delay)
        {
            ProviderName = provider;
            ModelId = model;
            _handler = async ct =>
            {
                await Task.Delay(delay, ct);
                return LlmResponse.Error("timed out");
            };
        }

        public async Task<LlmResponse> CompleteAsync(LlmRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return await _handler(cancellationToken);
        }
    }

    private class RecordingEventSink : IEventSink
    {
        public List<WorkflowEvent> Events { get; } = [];

        public Task PublishAsync(WorkflowEvent evt, CancellationToken cancellationToken = default)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }
    }
}