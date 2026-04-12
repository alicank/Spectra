---
description: "Build a custom IThreadManager for Spectra using Postgres, Redis, Cosmos DB, or any durable backend."
---

# Build Your Own Thread Manager

Spectra ships with `InMemoryThreadManager` for development and testing.

For production, you will usually want a durable backend such as Postgres, Redis, Cosmos DB, or your own storage layer.

A thread manager is the system that stores and queries workflow threads over time. It is useful when you want durable conversation or run grouping, thread cloning, retention rules, and operational cleanup.

---

## When to build a custom thread manager

Build a custom thread manager when you need:

- durable thread storage across restarts
- shared thread state across multiple app instances
- filtering and listing for operational UIs
- retention policies for old threads
- cloning threads for replay, sandboxing, or support workflows

A simple rule:

- use `InMemoryThreadManager` for local development
- build your own thread manager for production

---

## Step 1 — Implement `IThreadManager`

A thread manager handles more than CRUD.

It also supports:

- filtered listing
- cloning
- retention
- bulk deletion

```csharp
using Spectra.Contracts.Threading;
using Spectra.Contracts.Checkpointing;

public class PostgresThreadManager : IThreadManager
{
    // --- CRUD ---

    public Task<Thread> CreateAsync(Thread thread, CancellationToken ct = default)
    {
        // INSERT into threads table
    }

    public Task<Thread?> GetAsync(string threadId, CancellationToken ct = default)
    {
        // SELECT by thread_id
    }

    public Task<Thread> UpdateAsync(Thread thread, CancellationToken ct = default)
    {
        // UPDATE label, tags, metadata, updated_at
    }

    public Task DeleteAsync(string threadId, CancellationToken ct = default)
    {
        // DELETE thread + cascade to checkpoints via ICheckpointStore.PurgeAsync
    }

    // --- Query ---

    public Task<IReadOnlyList<Thread>> ListAsync(
        ThreadFilter? filter = null, CancellationToken ct = default)
    {
        // SELECT with WHERE clauses built from filter properties
        // ORDER BY updated_at DESC
    }

    // --- Clone ---

    public Task<Thread> CloneAsync(
        string sourceThreadId, string? newThreadId = null,
        bool cloneCheckpoints = true, CancellationToken ct = default)
    {
        // Load source thread
        // Create new thread record with SourceThreadId set
        // If cloneCheckpoints, use ICheckpointStore.ForkAsync to deep-copy state
    }

    // --- Retention ---

    public Task<RetentionResult> ApplyRetentionPolicyAsync(
        RetentionPolicy policy, ThreadFilter? filter = null,
        CancellationToken ct = default)
    {
        // Query threads matching filter
        // Delete threads older than MaxAge
        // Trim checkpoints exceeding MaxCheckpointsPerThread
    }

    public Task<int> BulkDeleteAsync(
        ThreadFilter filter, CancellationToken ct = default)
    {
        // DELETE FROM threads WHERE <filter conditions>
    }
}
```

---

## Step 2 — Understand what a thread manager stores

A thread manager stores `Thread` records, not workflow checkpoints themselves.

In practice, a thread usually represents a durable container for related workflow activity, such as:

- a conversation
- a long-running case
- a user session
- a support or approval flow

A good mental model is:

- one **thread**
- optional linked **checkpoints**
- optional **source thread** when cloned
- metadata for filtering and retention

---

## Step 3 — Design the storage model

A practical backend design usually includes:

| Concern | Recommendation |
| --- | --- |
| Primary key | Use `ThreadId` |
| Listing | Index by `UpdatedAt` descending |
| Filtering | Store tags, labels, and metadata in queryable form |
| Cloning | Store `SourceThreadId` on cloned threads |
| Checkpoint linkage | Keep a thread-to-run or thread-to-checkpoint relationship |
| Cleanup | Make delete and retention operations explicit |

The most important thing is consistency between the thread record and the checkpoint/history data it owns.

---

## Step 4 — Implement the CRUD methods

### `CreateAsync`

Create a new thread record.

```csharp
public Task<Thread> CreateAsync(Thread thread, CancellationToken ct = default)
{
    // Insert new thread
}
```

A good implementation should:

- validate required fields
- set creation and update timestamps
- return the stored thread shape

### `GetAsync`

Load one thread by ID.

```csharp
public Task<Thread?> GetAsync(string threadId, CancellationToken ct = default)
{
    // Load single thread or null
}
```

### `UpdateAsync`

Update mutable thread fields such as label, tags, or metadata.

```csharp
public Task<Thread> UpdateAsync(Thread thread, CancellationToken ct = default)
{
    // Update thread and return latest version
}
```

In most systems, this method should update `UpdatedAt` as part of the write.

### `DeleteAsync`

Delete a thread and its associated execution history.

```csharp
public Task DeleteAsync(string threadId, CancellationToken ct = default)
{
    // Delete thread and related checkpoint history
}
```

If your thread manager owns workflow history for the thread, make sure delete cascades correctly.

!!! warning
    A thread delete should not leave orphaned checkpoint history behind. Keep cleanup behavior explicit and consistent.

---

## Step 5 — Implement filtered listing

`ListAsync` is what powers thread browsing, admin screens, and operational tooling.

```csharp
public Task<IReadOnlyList<Thread>> ListAsync(
    ThreadFilter? filter = null, CancellationToken ct = default)
{
    // Query and order by UpdatedAt descending
}
```

A good listing implementation usually supports filters such as:

- thread ID
- label
- tags
- metadata values
- date ranges
- tenant or user scope

Return results ordered by most recently updated first unless your application has a different default.

---

## Step 6 — Implement cloning

Cloning creates a new thread derived from an existing one.

```csharp
public Task<Thread> CloneAsync(
    string sourceThreadId, string? newThreadId = null,
    bool cloneCheckpoints = true, CancellationToken ct = default)
{
    // Clone thread record and optionally clone checkpoint history
}
```

A correct clone implementation should:

1. load the source thread
2. create a new thread record
3. set `SourceThreadId`
4. optionally clone the underlying checkpoint history
5. return the new thread

If `cloneCheckpoints` is enabled, use your checkpoint store's fork or copy behavior so the cloned thread gets independent execution history.

Use cloning when you want:

- sandbox runs
- support/debug copies
- branching from an existing conversation or case

---

## Step 7 — Implement retention

Retention keeps thread storage from growing forever.

### `ApplyRetentionPolicyAsync`

Apply a policy to some or all threads.

```csharp
public Task<RetentionResult> ApplyRetentionPolicyAsync(
    RetentionPolicy policy, ThreadFilter? filter = null,
    CancellationToken ct = default)
{
    // Delete or trim based on policy
}
```

Typical retention rules include:

- delete threads older than a maximum age
- trim checkpoint history after a threshold
- scope cleanup to a filtered subset of threads

### `BulkDeleteAsync`

Delete many threads that match a filter.

```csharp
public Task<int> BulkDeleteAsync(
    ThreadFilter filter, CancellationToken ct = default)
{
    // Bulk delete matching threads
}
```

This is useful for:

- admin cleanup tools
- tenant offboarding
- test data cleanup
- policy enforcement jobs

---

## Step 8 — Think about checkpoint ownership

A thread manager often works closely with `ICheckpointStore`.

That means you should decide clearly:

- does deleting a thread also purge its checkpoints?
- does cloning a thread also clone its checkpoints?
- does retention trim only thread records, or thread records plus checkpoint history?

A good default is:

- deleting a thread deletes its related checkpoints
- cloning a thread optionally clones checkpoints
- retention can trim checkpoints independently when needed

If your thread model links to runs, store that relationship explicitly.

---

## Step 9 — Register the thread manager

Once implemented, register it with Spectra:

```csharp
services.AddSpectra(builder =>
{
    builder.AddThreadManager(new PostgresThreadManager());
});
```

After registration, Spectra can use your durable thread backend instead of the in-memory default.

---

## Testing your thread manager

At minimum, test these cases:

- create then get returns the same thread
- update changes mutable fields and updates timestamps
- delete removes the thread and related history
- list respects filters and ordering
- clone creates a new thread with `SourceThreadId`
- clone does not mutate the source thread
- retention deletes or trims the expected records
- bulk delete only affects matching threads
- cancellation tokens are honored

Example test shape:

```csharp
[Fact]
public async Task CreateAsync_Then_GetAsync_Returns_Thread()
{
    var manager = new PostgresThreadManager();

    var thread = new Thread
    {
        ThreadId = "thread-1",
        Label = "Support case"
    };

    await manager.CreateAsync(thread);
    var loaded = await manager.GetAsync("thread-1");

    Assert.NotNull(loaded);
    Assert.Equal("thread-1", loaded!.ThreadId);
}
```

!!! tip
    Cloning and retention are the two most important behaviors to test beyond CRUD. Those are also the places where backend-specific bugs usually appear first.

---

## Storage design tips

A few defaults work well for most backends:

| Concern | Recommendation |
| --- | --- |
| Primary identity | Use `ThreadId` |
| Source lineage | Store `SourceThreadId` on clones |
| Ordering | Sort by `UpdatedAt` descending |
| Filtering | Make tags and metadata queryable |
| Delete behavior | Cascade cleanly into checkpoint cleanup |
| Retention | Run as an explicit background or admin operation |

---

## Quick reference

| Task | How |
| --- | --- |
| Build a thread manager | Implement `IThreadManager` |
| Create a thread | `CreateAsync(thread)` |
| Load a thread | `GetAsync(threadId)` |
| Update metadata | `UpdateAsync(thread)` |
| Delete a thread | `DeleteAsync(threadId)` |
| List threads | `ListAsync(filter)` |
| Clone a thread | `CloneAsync(sourceThreadId, ...)` |
| Apply retention | `ApplyRetentionPolicyAsync(policy, filter)` |
| Delete in bulk | `BulkDeleteAsync(filter)` |
| Register in Spectra | `builder.AddThreadManager(new YourManager())` |

---

## A simple mental model

A thread manager is the durable catalog for workflow threads.

It must answer:

- does this thread exist?
- what does it look like now?
- which threads match this filter?
- can I clone this thread?
- can I clean up old threads safely?

If your store can answer those reliably, it is a good Spectra thread backend.