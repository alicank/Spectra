# Memory & Threading

Spectra provides two persistence mechanisms: **memory** for cross-run knowledge and **threading** for managing conversation or run lifecycles.

---

## Long-Term Memory

Memory allows agents and workflows to store and recall information across runs. Unlike workflow state, which belongs to a single execution, memory is stored behind an `IMemoryStore`.

### IMemoryStore

```csharp
public interface IMemoryStore
{
    Task<MemoryEntry?> GetAsync(string @namespace, string key, CancellationToken ct = default);
    Task SetAsync(string @namespace, string key, MemoryEntry entry, CancellationToken ct = default);
    Task DeleteAsync(string @namespace, string key, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryEntry>> ListAsync(string @namespace, CancellationToken ct = default);
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(MemorySearchQuery query, CancellationToken ct = default);
    Task PurgeAsync(string @namespace, CancellationToken ct = default);
    MemoryStoreCapabilities Capabilities { get; }
}
```

### Memory Entries

Each memory entry has a namespace, key, content, optional tags, optional metadata, timestamps, optional expiration, and schema version:

```csharp
var entry = new MemoryEntry
{
    Namespace = "user-preferences",
    Key = "language",
    Content = "User prefers French for all communications",
    Tags = ["preferences", "language"],
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "conversation-123",
        ["nodeId"] = "onboarding"
    },
    CreatedAt = DateTimeOffset.UtcNow,
    UpdatedAt = DateTimeOffset.UtcNow
};

await memoryStore.SetAsync("user-preferences", "language", entry);
```

You can also use `MemoryEntry.Create(...)` and `entry.GetValue<T>()` when storing typed JSON content.

### Searching Memory

```csharp
var results = await memoryStore.SearchAsync(new MemorySearchQuery
{
    Namespace = "user-preferences",
    Text = "language preference",
    Tags = ["preferences"],
    MaxResults = 5
});

foreach (var result in results)
{
    Console.WriteLine($"{result.Entry.Key}: {result.Entry.Content}");
}
```

`MemorySearchQuery` also supports `MetadataFilters` and `IncludeExpired`. Stores that do not support search can return an empty list; check `memoryStore.Capabilities`.

### Built-in Stores

**InMemoryMemoryStore** — For testing and prototyping:

```csharp
builder.AddInMemoryMemory();
```

**FileMemoryStore** — JSON files on disk:

```csharp
builder.AddFileMemory("./memory");
```

For production, implement `IMemoryStore` backed by your database or vector store. See the [Build Your Own Memory Store](../guides/build-your-own-memory-store.md) guide.

---

## Memory Tools

Agents can interact with memory during their tool-calling loop through two built-in tools:

| Tool | Description |
|------|-------------|
| `store_memory` | Save information to memory during an agent loop. |
| `recall_memory` | Query memory for relevant past information. |

`store_memory` accepts `key`, `content`, optional `namespace`, and optional comma-separated `tags`.

`recall_memory` accepts `query`, optional `namespace`, optional comma-separated `tags`, and optional `max_results`.

The tool classes are `StoreMemoryTool` and `RecallMemoryTool`. They can be registered like other tools if you want explicit control.

### Auto-Injection

`MemoryOptions.AutoInjectAgentTools` is intended to automatically add memory tools when an agent has supervisor worker delegation configured.

```csharp
builder.AddMemory(new InMemoryMemoryStore(), options =>
{
    options.AutoInjectAgentTools = true;
    options.DefaultNamespace = MemoryNamespace.Global;
});
```

In the current default registration path, this option is not wired into `AgentStep`, so explicit tool registration is the reliable path if you need memory tools in agent loops.

---

## Memory Steps

For workflow-level memory operations **outside** agent loops, Spectra provides two dedicated step types. Use these when you want explicit, deterministic memory operations as nodes in your workflow graph.

### MemoryStoreStep

Persists data to long-term memory.

**StepType:** `"memory.store"`

#### Inputs

| Input | Type | Default | Description |
|-------|------|---------|-------------|
| `namespace` | `string` | `"global"` | Memory scope. Namespaces isolate entries from each other. |
| `key` | `string` | **required** | Unique identifier for the entry within the namespace. |
| `content` | `string` | **required** | The data to store. |
| `tags` | `string` | — | Comma-separated tags for filtering, for example `"preferences,user"`. |

#### Outputs

| Output | Type | Description |
|--------|------|-------------|
| `stored` | `bool` | `true` if the operation succeeded. |
| `key` | `string` | The key that was stored. |
| `action` | `string` | `"created"` for new entries, `"updated"` for existing ones. |

#### Example

```csharp
var workflow = WorkflowBuilder.Create("save-preference")
    .AddNode("store", "memory.store", node => node
        .WithParameter("namespace", "user-preferences")
        .WithParameter("key", "theme")
        .WithParameter("content", "{{inputs.selectedTheme}}")
        .WithParameter("tags", "preferences,ui"))
    .Build();
```

#### Behavior

- If an entry with the same namespace and key already exists, it is **updated** and the original `CreatedAt` timestamp is preserved.
- Metadata is automatically populated with `source = "step"`, `nodeId`, `runId`, and `workflowId`.
- If no `IMemoryStore` is configured, the step fails with a clear error message.

---

### MemoryRecallStep

Retrieves data from long-term memory. Supports three retrieval modes: exact key lookup, text search, and listing.

**StepType:** `"memory.recall"`

#### Inputs

| Input | Type | Default | Description |
|-------|------|---------|-------------|
| `namespace` | `string` | `"global"` | Memory scope to search within. |
| `key` | `string` | — | Exact key lookup. Takes precedence over `query`. |
| `query` | `string` | — | Search text for finding relevant memories. |
| `tags` | `string` | — | Comma-separated tag filter, only used with `query`. |
| `maxResults` | `int` | `10` | Maximum entries to return. |

#### Outputs

| Output | Type | Description |
|--------|------|-------------|
| `memories` | `List<MemoryEntry>` | The recalled memory entries. |
| `count` | `int` | Number of entries returned. |
| `found` | `bool` | `true` if at least one entry was found. |

#### Retrieval Modes

The step picks a mode based on which inputs are set:

| Mode | Trigger | Behavior |
|------|---------|----------|
| **Key lookup** | `key` is set | Returns the single entry with that exact key, or an empty list. |
| **Search** | `query` is set and `key` is not set | Searches by text, optionally filtered by tags. Uses `IMemoryStore.SearchAsync`. |
| **List** | Neither `key` nor `query` | Lists recent entries in the namespace, up to `maxResults`. |

#### Example — Store and Recall

```csharp
var workflow = WorkflowBuilder.Create("remember-and-recall")
    .AddNode("store", "memory.store", node => node
        .WithParameter("namespace", "user-preferences")
        .WithParameter("key", "favorite-language")
        .WithParameter("content", "{{inputs.language}}")
        .WithParameter("tags", "preferences,onboarding"))
    .AddNode("recall", "memory.recall", node => node
        .WithParameter("namespace", "user-preferences")
        .WithParameter("key", "favorite-language"))
    .AddEdge("store", "recall")
    .Build();
```

!!! tip "Memory Steps vs Memory Tools"
    **Memory Steps** (`MemoryStoreStep`, `MemoryRecallStep`) are workflow nodes — deterministic, explicit, part of the graph. Use them in DAG-style workflows where you control exactly when memory is read or written.

    **Memory Tools** (`store_memory`, `recall_memory`) are called by the LLM during an agent loop. The agent decides when and what to store or recall. Use them when you want the agent to manage its own memory autonomously.

---

## Threading (Lifecycle Management)

Threads are first-class lifecycle records for grouping workflow runs and checkpoint history. They are useful for querying, cloning, retaining, and deleting long-running conversations or run groups.

`SessionStep` maintains conversation state through workflow state and checkpoints. `IThreadManager` manages thread records and their linked checkpoints; it does not store a list of chat messages.

### IThreadManager

```csharp
public interface IThreadManager
{
    Task<Thread> CreateAsync(Thread thread, CancellationToken ct = default);
    Task<Thread?> GetAsync(string threadId, CancellationToken ct = default);
    Task<Thread> UpdateAsync(Thread thread, CancellationToken ct = default);
    Task DeleteAsync(string threadId, CancellationToken ct = default);
    Task<IReadOnlyList<Thread>> ListAsync(ThreadFilter? filter = null, CancellationToken ct = default);
    Task<Thread> CloneAsync(string sourceThreadId, string? newThreadId = null, bool cloneCheckpoints = true, CancellationToken ct = default);
    Task<RetentionResult> ApplyRetentionPolicyAsync(RetentionPolicy policy, ThreadFilter? filter = null, CancellationToken ct = default);
    Task<int> BulkDeleteAsync(ThreadFilter filter, CancellationToken ct = default);
}
```

### Thread Structure

A thread stores lifecycle metadata and the current run ID:

```csharp
public sealed record Thread
{
    public required string ThreadId { get; init; }
    public required string WorkflowId { get; init; }
    public string? TenantId { get; init; }
    public string? UserId { get; init; }
    public string? Label { get; init; }
    public IReadOnlyList<string> Tags { get; init; }
    public required string RunId { get; init; }
    public Dictionary<string, string> Metadata { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? SourceThreadId { get; init; }
}
```

### Registration

Use the built-in in-memory manager:

```csharp
builder.AddInMemoryThreadManager();
```

Or register your own:

```csharp
builder.AddThreadManager(new PostgresThreadManager());
```

### Retention Policies

Retention is applied through the manager:

```csharp
var result = await threadManager.ApplyRetentionPolicyAsync(new RetentionPolicy
{
    MaxAge = TimeSpan.FromDays(30),
    MaxCheckpointsPerThread = 100
});
```

`MaxAge` deletes old threads based on `UpdatedAt`. `MaxCheckpointsPerThread` trims checkpoint history when a checkpoint store is attached to the thread manager. `ApplyToStatus` can limit age-based deletion to threads whose latest checkpoint has a matching status.

### Built-in Implementations

**InMemoryThreadManager** — For testing and short-lived applications. When constructed with an `ICheckpointStore`, deleting a thread purges its run checkpoints, and cloning can fork checkpoint history.

For production, implement `IThreadManager` backed by a database. See the [Build Your Own Thread Manager](../guides/build-your-own-thread-manager.md) guide.
