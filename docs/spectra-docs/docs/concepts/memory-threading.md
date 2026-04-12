# Memory & Threading

Spectra provides two persistence mechanisms: **memory** for cross-session knowledge and **threading** for conversation management.

---

## Long-Term Memory

Memory allows agents and workflows to store and recall information across runs. Unlike workflow state (which lives within a single execution), memory persists indefinitely.

### IMemoryStore

```csharp
public interface IMemoryStore
{
    Task SetAsync(string ns, string key, MemoryEntry entry, CancellationToken ct = default);
    Task<MemoryEntry?> GetAsync(string ns, string key, CancellationToken ct = default);
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(MemorySearchQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryEntry>> ListAsync(string ns, CancellationToken ct = default);
    Task DeleteAsync(string ns, string key, CancellationToken ct = default);
    MemoryStoreCapabilities Capabilities { get; }
}
```

### Memory Entries

Each memory entry has a namespace, key, content, and optional tags and metadata:

```csharp
var entry = new MemoryEntry
{
    Namespace = "user-preferences",
    Key = "language",
    Content = "User prefers French for all communications",
    Tags = new List<string> { "preferences", "language" },
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

### Searching Memory

```csharp
var results = await memoryStore.SearchAsync(new MemorySearchQuery
{
    Namespace = "user-preferences",
    Text = "language preference",
    Tags = new List<string> { "preferences" },
    MaxResults = 5
});

foreach (var result in results)
{
    Console.WriteLine($"{result.Entry.Key}: {result.Entry.Content}");
}
```

### Built-in Stores

**InMemoryMemoryStore** — For testing and prototyping:

```csharp
builder.AddMemoryStore<InMemoryMemoryStore>();
```

**FileMemoryStore** — JSON files on disk:

```csharp
builder.AddMemoryStore(new FileMemoryStore("./memory"));
```

For production, implement `IMemoryStore` backed by a vector database (Weaviate, Qdrant, Pinecone) for semantic search. See the [Build Your Own Memory Store](../others/build-your-own-memory-store.md) guide.

---

## Memory Tools

Agents can interact with memory during their tool-calling loop through two built-in tools:

| Tool | Description |
|------|-------------|
| `store_memory` | Save information to memory during an agent loop. |
| `recall_memory` | Query memory for relevant past information. |

```csharp
builder.AddAgent("assistant", agent => agent
    .WithProvider("openai")
    .WithTools("store_memory", "recall_memory")
    .WithSystemPrompt("You can remember information across conversations."));
```

### Auto-Injection

When `MemoryOptions.AutoInjectAgentTools` is `true`, memory tools are automatically added to every agent that has a `DelegateToAgentTool` configured — you don't need to list them manually.

```csharp
builder.AddMemory(options =>
{
    options.AutoInjectAgentTools = true;
    options.DefaultNamespace = MemoryNamespace.From("global");
});
```

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
| `content` | `string` | **required** | The data to store. Non-string values are serialized. |
| `tags` | `string` | — | Comma-separated tags for filtering (e.g. `"preferences,user"`). |

#### Outputs

| Output | Type | Description |
|--------|------|-------------|
| `stored` | `bool` | `true` if the operation succeeded. |
| `key` | `string` | The key that was stored. |
| `action` | `string` | `"created"` for new entries, `"updated"` for existing ones. |

#### Example

```csharp
var workflow = Spectra.Workflow("save-preference")
    .AddStep("store", new MemoryStoreStep(), inputs: new
    {
        @namespace = "user-preferences",
        key = "theme",
        content = "{{inputs.selectedTheme}}",
        tags = "preferences,ui"
    })
    .Build();
```

#### Behavior

- If an entry with the same namespace and key already exists, it is **updated** (the `CreatedAt` timestamp is preserved).
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
| `tags` | `string` | — | Comma-separated tag filter (only with `query`). |
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
| **Key lookup** | `key` is set | Returns the single entry with that exact key (or empty). |
| **Search** | `query` is set (no `key`) | Searches by text, optionally filtered by tags. Uses `IMemoryStore.SearchAsync`. |
| **List** | Neither `key` nor `query` | Lists recent entries in the namespace (up to `maxResults`). |

#### Example — RAG with Memory

```csharp
var workflow = Spectra.Workflow("remember-and-answer")
    .AddStep("recall", new MemoryRecallStep(), inputs: new
    {
        query = "{{inputs.question}}",
        @namespace = "knowledge-base",
        maxResults = 5
    })
    .AddPromptStep("answer", agent: "openai", inputs: new
    {
        userPrompt = """
            Context from memory:
            {{nodes.recall.output.memories}}

            Question: {{inputs.question}}

            Answer based on the context above.
            """
    })
    .Edge("recall", "answer")
    .Build();
```

!!! tip "Memory Steps vs Memory Tools"
    **Memory Steps** (`MemoryStoreStep`, `MemoryRecallStep`) are workflow nodes — deterministic, explicit, part of the graph. Use them in DAG-style workflows where you control exactly when memory is read or written.

    **Memory Tools** (`store_memory`, `recall_memory`) are called by the LLM during an agent loop. The agent decides when and what to store or recall. Use them when you want the agent to manage its own memory autonomously.

---

## Threading (Conversation Management)

Threads track conversation history across multiple interactions. This is how `SessionStep` maintains context between user turns.

### IThreadManager

```csharp
public interface IThreadManager
{
    Task<Thread> CreateAsync(string? workflowName = null, CancellationToken ct = default);
    Task<Thread?> GetAsync(string threadId, CancellationToken ct = default);
    Task UpdateAsync(Thread thread, CancellationToken ct = default);
    Task<IReadOnlyList<Thread>> ListAsync(ThreadFilter? filter = null, CancellationToken ct = default);
    Task DeleteAsync(string threadId, CancellationToken ct = default);
}
```

### Thread Structure

A thread holds the message history and metadata:

```csharp
public class Thread
{
    public string Id { get; }
    public List<Message> Messages { get; }
    public Dictionary<string, object> Metadata { get; }
    public DateTime CreatedAt { get; }
    public DateTime UpdatedAt { get; }
}
```

### Retention Policies

Control how threads are managed over time:

```csharp
builder.AddThreadManager<InMemoryThreadManager>(new RetentionPolicy
{
    MaxMessages = 100,
    MaxAge = TimeSpan.FromDays(30)
});
```

### Built-in Implementations

**InMemoryThreadManager** — For testing and short-lived applications.

For production, implement `IThreadManager` backed by a database. See the [Build Your Own Thread Manager](../others/build-your-own-thread-manager.md) guide.