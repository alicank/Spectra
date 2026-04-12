---
description: "Build a custom IMemoryStore for Spectra using Redis, Postgres, Cosmos DB, or any durable backend."
---

# Build Your Own Memory Store

Spectra ships with `InMemoryMemoryStore` and `FileMemoryStore` for development and testing.

For production, you will usually want a durable backend such as Redis, Postgres, Cosmos DB, or your own storage layer.

This guide shows the contract, the design decisions, and the minimum behavior your store should implement.

---

## When to build a custom memory store

Build a custom memory store when you need:

- durable memory across process restarts
- shared memory across multiple app instances
- search over stored entries
- TTL and expiration support
- tenant-aware or production-grade storage

A simple rule:

- use in-memory or file stores for local development
- build your own store for production

---

## Step 1 — Implement `IMemoryStore`

A memory store implements CRUD, listing, searching, and namespace cleanup.

```csharp
using Spectra.Contracts.Memory;

public class RedisMemoryStore : IMemoryStore
{
    public MemoryStoreCapabilities Capabilities => new()
    {
        CanSearch = true,
        CanExpire = true,
        CanFilterByTags = true,
        CanFilterByMetadata = false
    };

    public Task<MemoryEntry?> GetAsync(
        string @namespace, string key, CancellationToken ct = default)
    {
        // HGET spectra:memory:{namespace} {key}
        // Deserialize JSON -> MemoryEntry
        // Check ExpiresAt if not using Redis-native TTL
    }

    public Task SetAsync(
        string @namespace, string key, MemoryEntry entry, CancellationToken ct = default)
    {
        // Serialize entry -> JSON
        // HSET spectra:memory:{namespace} {key} {json}
        // If entry.ExpiresAt is set, use EXPIREAT on the field
    }

    public Task DeleteAsync(
        string @namespace, string key, CancellationToken ct = default)
    {
        // HDEL spectra:memory:{namespace} {key}
    }

    public Task<IReadOnlyList<MemoryEntry>> ListAsync(
        string @namespace, CancellationToken ct = default)
    {
        // HGETALL spectra:memory:{namespace}
        // Deserialize each value, filter expired, sort by UpdatedAt desc
    }

    public Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        MemorySearchQuery query, CancellationToken ct = default)
    {
        // If using RediSearch: FT.SEARCH with filters
        // Otherwise: fall back to HGETALL + in-memory filtering
    }

    public Task PurgeAsync(string @namespace, CancellationToken ct = default)
    {
        // DEL spectra:memory:{namespace}
    }
}
```

---

## Step 2 — Understand the data model

A memory store persists `MemoryEntry` objects.

In practice, each entry usually contains:

- a namespace
- a key
- content
- timestamps
- optional tags
- optional metadata
- optional expiration

Your store should treat `namespace + key` as the logical identity of an entry.

A good storage model is:

- one partition per namespace
- one record per key inside that namespace

---

## Step 3 — Advertise capabilities correctly

`Capabilities` tells Spectra what your store can do natively.

```csharp
public MemoryStoreCapabilities Capabilities => new()
{
    CanSearch = true,
    CanExpire = true,
    CanFilterByTags = true,
    CanFilterByMetadata = false
};
```

### What the flags mean

| Capability | Meaning |
| --- | --- |
| `CanSearch` | The store can search entries, not just list by namespace |
| `CanExpire` | The store supports expiration or TTL behavior |
| `CanFilterByTags` | The store can filter by entry tags |
| `CanFilterByMetadata` | The store can filter by metadata fields |

Only advertise capabilities your store really supports.

If a capability is not truly supported, it is better to return `false` and fall back to simpler behavior than to claim support and return inconsistent results.

---

## Step 4 — Implement the core operations

### `GetAsync`

Load one entry by namespace and key.

```csharp
public Task<MemoryEntry?> GetAsync(
    string @namespace, string key, CancellationToken ct = default)
{
    // Load one record
    // Return null if not found
}
```

Use this for direct lookup.

If the entry is expired, treat it as missing.

### `SetAsync`

Create or replace an entry.

```csharp
public Task SetAsync(
    string @namespace, string key, MemoryEntry entry, CancellationToken ct = default)
{
    // Upsert the record
}
```

This method should:

- overwrite the existing value for the same namespace/key
- persist updated timestamps
- apply expiration if supported

### `DeleteAsync`

Remove one entry.

```csharp
public Task DeleteAsync(
    string @namespace, string key, CancellationToken ct = default)
{
    // Delete one record
}
```

### `ListAsync`

Return entries for a namespace.

```csharp
public Task<IReadOnlyList<MemoryEntry>> ListAsync(
    string @namespace, CancellationToken ct = default)
{
    // Return all entries in the namespace
}
```

A good default is to:

- exclude expired entries
- sort by `UpdatedAt` descending

### `PurgeAsync`

Delete everything in a namespace.

```csharp
public Task PurgeAsync(string @namespace, CancellationToken ct = default)
{
    // Remove all entries in the namespace
}
```

This is useful for resetting memory scopes cleanly.

---

## Step 5 — Implement `SearchAsync`

`SearchAsync` is where stores differ most.

```csharp
public Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
    MemorySearchQuery query, CancellationToken ct = default)
{
    // Native search if available
    // Otherwise fallback to scan + filter
}
```

There are usually two strategies:

### Native search

Use the backend's search engine directly.

Examples:

- RediSearch
- Postgres full-text search
- Cosmos indexed queries
- Elasticsearch

This is best when you need scale and filtering.

### Scan and filter

Load entries and filter in application code.

This is simpler and often fine for low-volume namespaces, but it does not scale as well.

!!! tip
    If your backend does not support native search yet, it is still valid to implement `SearchAsync` with list-and-filter logic first, then optimize later.

---

## Step 6 — Handle expiration

If your store supports expiration, make sure expired entries do not behave like live memory.

There are two common patterns:

### Native TTL

Let the backend expire records automatically.

Examples:

- Redis TTL
- Cosmos TTL
- database jobs that delete expired rows

### Soft expiration

Store `ExpiresAt` and filter expired records in your code.

This is simpler to implement, but requires you to check expiration in:

- `GetAsync`
- `ListAsync`
- `SearchAsync`

A good rule:

- if expired, do not return it
- optionally delete it lazily when encountered

---

## Step 7 — Register the store

Once implemented, register it with Spectra:

```csharp
services.AddSpectra(builder =>
{
    builder.AddMemory(new RedisMemoryStore());
});
```

You can also configure memory behavior at registration time:

```csharp
services.AddSpectra(builder =>
{
    builder.AddMemory(new RedisMemoryStore(), options =>
    {
        options.AutoInjectAgentTools = true;
    });
});
```

After registration, memory-aware steps and agents can use it through the normal Spectra memory APIs.

---

## Testing your store

At minimum, test these cases:

- set then get returns the same entry
- delete removes the entry
- list returns only entries from the requested namespace
- purge removes the entire namespace
- expired entries are not returned
- search returns correct matches
- cancellation tokens are honored

A simple test shape looks like this:

```csharp
[Fact]
public async Task SetAsync_Then_GetAsync_Returns_Entry()
{
    var store = new RedisMemoryStore();

    var entry = new MemoryEntry
    {
        Namespace = "prefs",
        Key = "theme",
        Content = "dark",
        UpdatedAt = DateTimeOffset.UtcNow
    };

    await store.SetAsync("prefs", "theme", entry);
    var loaded = await store.GetAsync("prefs", "theme");

    Assert.NotNull(loaded);
    Assert.Equal("dark", loaded!.Content);
}
```

!!! warning
    Test expiration and search behavior explicitly. Those are the two areas where custom stores most often behave inconsistently.

---

## Storage design tips

A few practical defaults work well for most backends:

| Concern | Recommendation |
| --- | --- |
| Primary identity | Use `namespace + key` |
| Namespace isolation | Partition or prefix by namespace |
| Sorting | Sort by `UpdatedAt` descending |
| Expiration | Prefer native TTL when available |
| Search fallback | Start with scan/filter if needed |
| Serialization | Store `MemoryEntry` as JSON unless your backend needs a typed schema |

---

## Quick reference

| Task | How |
| --- | --- |
| Build a memory store | Implement `IMemoryStore` |
| Advertise features | Return `MemoryStoreCapabilities` |
| Read one entry | `GetAsync(namespace, key)` |
| Write one entry | `SetAsync(namespace, key, entry)` |
| Delete one entry | `DeleteAsync(namespace, key)` |
| List a namespace | `ListAsync(namespace)` |
| Search memory | `SearchAsync(query)` |
| Clear a namespace | `PurgeAsync(namespace)` |
| Register in Spectra | `builder.AddMemory(new YourStore())` |

---

## A simple mental model

A memory store is just a durable key-value system with namespaces, optional search, and optional expiration.

If your store can reliably answer:

- get this entry
- set this entry
- list this namespace
- search these entries
- purge this namespace

then it is a good Spectra memory backend.