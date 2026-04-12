# MemoryStoreRecall

A two-node workflow that stores a user preference to long-term memory, then immediately recalls it — proving that memory persists across nodes within a single run. No API key needed.

## What it demonstrates

- **MemoryStoreStep** (`memory.store`) — persists a fact with namespace, key, content, and tags
- **MemoryRecallStep** (`memory.recall`) — retrieves it by exact key lookup
- **InMemoryMemoryStore** — zero-config memory backend for development
- **Namespace scoping** — entries are isolated by namespace (`user-preferences`)
- **Cross-node persistence** — data written by one node is available to the next

## The graph

```
┌───────────┐     ┌────────────┐
│   store   │────▶│   recall   │
└───────────┘     └────────────┘
 memory.store      memory.recall
 ns: user-prefs    ns: user-prefs
 key: fav-lang     key: fav-lang
```

## Run it

```bash
cd samples/MemoryStoreRecall
dotnet run
```

Pass a different language as an argument:

```bash
dotnet run -- Rust
dotnet run -- Python
```

## Expected output

```
Storing preference: language = "C#"

  [memory.store] → stored "C#" at user-preferences/favorite-language
  [memory.recall] → found 1 entry in user-preferences

Store  → stored: True, action: created, key: favorite-language
Recall → found: True, count: 1
         namespace: user-preferences, key: favorite-language, content: "C#"

Errors: 0
```

## What's next

- [**PromptBasic**](../PromptBasic/) — add an LLM node that uses the recalled data
- [**AgentWithMcp**](../AgentWithMcp/) — agent-driven memory with `store_memory` / `recall_memory` tools
- [Memory & Threading docs](../../docs/spectra-docs/docs/concepts/memory-threading.md) — full reference