using Spectra.Kernel.Caching;
using Xunit;

namespace Spectra.Tests.Caching;

public class InMemoryCacheStoreTests
{
    [Fact]
    public async Task GetAsync_ReturnsNull_WhenKeyNotFound()
    {
        using var store = new InMemoryCacheStore();

        var result = await store.GetAsync<TestData>("missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAndGet_RoundTrips()
    {
        using var store = new InMemoryCacheStore();
        var data = new TestData { Value = "hello" };

        await store.SetAsync("key1", data);
        var result = await store.GetAsync<TestData>("key1");

        Assert.NotNull(result);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_AfterTtlExpires()
    {
        using var store = new InMemoryCacheStore();
        var data = new TestData { Value = "expires" };

        await store.SetAsync("key1", data, TimeSpan.FromMilliseconds(50));
        await Task.Delay(100);
        var result = await store.GetAsync<TestData>("key1");

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_DeletesEntry()
    {
        using var store = new InMemoryCacheStore();
        await store.SetAsync("key1", new TestData { Value = "bye" });

        await store.RemoveAsync("key1");
        var result = await store.GetAsync<TestData>("key1");

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_NoOp_WhenKeyMissing()
    {
        using var store = new InMemoryCacheStore();

        await store.RemoveAsync("nope"); // should not throw
    }

    private sealed class TestData
    {
        public string Value { get; init; } = default!;
    }
}