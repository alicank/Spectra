using Spectra.Contracts.Memory;
using Xunit;

namespace Spectra.Tests.Memory;

/// <summary>
/// Contract compliance test suite for <see cref="IMemoryStore"/> implementations.
/// Inherit this class, implement <see cref="CreateStore"/>, and all contract tests
/// will run automatically against your store.
/// </summary>
public abstract class MemoryStoreTestBase<T> where T : IMemoryStore
{
    protected abstract T CreateStore();

    private const string TestNamespace = "test.ns";

    private static MemoryEntry CreateEntry(
        string? key = null,
        string? @namespace = null,
        string content = "\"hello\"")
    {
        return new MemoryEntry
        {
            Key = key ?? Guid.NewGuid().ToString(),
            Namespace = @namespace ?? TestNamespace,
            Content = content
        };
    }

    [Fact]
    public async Task SetAndGet_RoundTrips()
    {
        var store = CreateStore();
        var entry = CreateEntry(key: "k1");

        await store.SetAsync(TestNamespace, "k1", entry);
        var loaded = await store.GetAsync(TestNamespace, "k1");

        Assert.NotNull(loaded);
        Assert.Equal("k1", loaded!.Key);
        Assert.Equal(TestNamespace, loaded.Namespace);
        Assert.Equal(entry.Content, loaded.Content);
    }

    [Fact]
    public async Task Get_NonExistent_ReturnsNull()
    {
        var store = CreateStore();

        var result = await store.GetAsync(TestNamespace, "does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public async Task Set_OverwritesExisting()
    {
        var store = CreateStore();
        var original = CreateEntry(key: "k1", content: "\"v1\"");
        await store.SetAsync(TestNamespace, "k1", original);

        var updated = original with { Content = "\"v2\"" };
        await store.SetAsync(TestNamespace, "k1", updated);

        var loaded = await store.GetAsync(TestNamespace, "k1");
        Assert.NotNull(loaded);
        Assert.Equal("\"v2\"", loaded!.Content);
    }

    [Fact]
    public async Task Delete_RemovesEntry()
    {
        var store = CreateStore();
        await store.SetAsync(TestNamespace, "k1", CreateEntry(key: "k1"));

        await store.DeleteAsync(TestNamespace, "k1");

        var loaded = await store.GetAsync(TestNamespace, "k1");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task Delete_NonExistent_DoesNotThrow()
    {
        var store = CreateStore();

        var ex = await Record.ExceptionAsync(
            () => store.DeleteAsync(TestNamespace, "nope"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ListAsync_ReturnsEntriesInNamespace()
    {
        var store = CreateStore();
        await store.SetAsync(TestNamespace, "a", CreateEntry(key: "a"));
        await store.SetAsync(TestNamespace, "b", CreateEntry(key: "b"));
        await store.SetAsync("other.ns", "c", CreateEntry(key: "c", @namespace: "other.ns"));

        var results = await store.ListAsync(TestNamespace);

        Assert.Equal(2, results.Count);
        Assert.All(results, e => Assert.Equal(TestNamespace, e.Namespace));
    }

    [Fact]
    public async Task ListAsync_EmptyNamespace_ReturnsEmpty()
    {
        var store = CreateStore();

        var results = await store.ListAsync("empty.ns");

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_ByText_FindsMatches()
    {
        var store = CreateStore();
        await store.SetAsync(TestNamespace, "planets",
            CreateEntry(key: "planets", content: "\"Earth orbits the Sun\""));
        await store.SetAsync(TestNamespace, "food",
            CreateEntry(key: "food", content: "\"Pizza is delicious\""));

        var query = new MemorySearchQuery
        {
            Namespace = TestNamespace,
            Text = "Earth"
        };

        var results = await store.SearchAsync(query);

        if (!store.Capabilities.CanSearch)
        {
            Assert.Empty(results);
            return;
        }

        Assert.Single(results);
        Assert.Equal("planets", results[0].Entry.Key);
    }

    [Fact]
    public async Task Search_ByTags_FiltersCorrectly()
    {
        var store = CreateStore();
        await store.SetAsync(TestNamespace, "t1",
            new MemoryEntry
            {
                Key = "t1",
                Namespace = TestNamespace,
                Content = "\"a\"",
                Tags = ["important", "finance"]
            });
        await store.SetAsync(TestNamespace, "t2",
            new MemoryEntry
            {
                Key = "t2",
                Namespace = TestNamespace,
                Content = "\"b\"",
                Tags = ["trivial"]
            });

        var query = new MemorySearchQuery
        {
            Namespace = TestNamespace,
            Tags = ["important"]
        };

        var results = await store.SearchAsync(query);

        if (!store.Capabilities.CanFilterByTags)
        {
            // Store doesn't support tag filtering — skip assertion
            return;
        }

        Assert.Single(results);
        Assert.Equal("t1", results[0].Entry.Key);
    }

    [Fact]
    public async Task Search_ByMetadata_FiltersCorrectly()
    {
        var store = CreateStore();
        await store.SetAsync(TestNamespace, "m1",
            new MemoryEntry
            {
                Key = "m1",
                Namespace = TestNamespace,
                Content = "\"a\"",
                Metadata = new() { ["source"] = "agent-1" }
            });
        await store.SetAsync(TestNamespace, "m2",
            new MemoryEntry
            {
                Key = "m2",
                Namespace = TestNamespace,
                Content = "\"b\"",
                Metadata = new() { ["source"] = "agent-2" }
            });

        var query = new MemorySearchQuery
        {
            Namespace = TestNamespace,
            MetadataFilters = new() { ["source"] = "agent-1" }
        };

        var results = await store.SearchAsync(query);

        if (!store.Capabilities.CanFilterByMetadata)
            return;

        Assert.Single(results);
        Assert.Equal("m1", results[0].Entry.Key);
    }

    [Fact]
    public async Task Purge_RemovesAllInNamespace()
    {
        var store = CreateStore();
        await store.SetAsync(TestNamespace, "a", CreateEntry(key: "a"));
        await store.SetAsync(TestNamespace, "b", CreateEntry(key: "b"));
        await store.SetAsync("keep.ns", "c", CreateEntry(key: "c", @namespace: "keep.ns"));

        await store.PurgeAsync(TestNamespace);

        var purged = await store.ListAsync(TestNamespace);
        var kept = await store.ListAsync("keep.ns");

        Assert.Empty(purged);
        Assert.Single(kept);
    }

    [Fact]
    public async Task Purge_NonExistent_DoesNotThrow()
    {
        var store = CreateStore();

        var ex = await Record.ExceptionAsync(
            () => store.PurgeAsync("nope"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ExpiredEntry_NotReturnedByGet()
    {
        var store = CreateStore();
        var expired = new MemoryEntry
        {
            Key = "exp",
            Namespace = TestNamespace,
            Content = "\"old\"",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-10)
        };
        await store.SetAsync(TestNamespace, "exp", expired);

        var loaded = await store.GetAsync(TestNamespace, "exp");

        if (!store.Capabilities.CanExpire)
            return; // Store doesn't handle expiration

        Assert.Null(loaded);
    }

    [Fact]
    public async Task TypedExtensions_RoundTrip()
    {
        var store = CreateStore();
        var entry = MemoryEntryExtensions.Create(
            TestNamespace, "typed", new { Name = "Alice", Age = 30 });

        await store.SetAsync(TestNamespace, "typed", entry);
        var loaded = await store.GetAsync(TestNamespace, "typed");

        Assert.NotNull(loaded);

        // Verify content is valid JSON with expected data
        Assert.Contains("Alice", loaded!.Content);
    }

    [Fact]
    public async Task NamespaceIsolation_GetDoesNotCrossNamespaces()
    {
        var store = CreateStore();
        await store.SetAsync("ns-a", "shared-key",
            CreateEntry(key: "shared-key", @namespace: "ns-a", content: "\"from-a\""));
        await store.SetAsync("ns-b", "shared-key",
            CreateEntry(key: "shared-key", @namespace: "ns-b", content: "\"from-b\""));

        var fromA = await store.GetAsync("ns-a", "shared-key");
        var fromB = await store.GetAsync("ns-b", "shared-key");

        Assert.NotNull(fromA);
        Assert.NotNull(fromB);
        Assert.Equal("\"from-a\"", fromA!.Content);
        Assert.Equal("\"from-b\"", fromB!.Content);
    }
}