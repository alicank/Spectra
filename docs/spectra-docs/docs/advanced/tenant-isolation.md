# Tenant Isolation & Identity

Spectra treats caller identity as a first-class runtime primitive. A `RunContext` carrying `TenantId`, `UserId`, roles, and claims is injected at workflow invocation and automatically propagated through every step, every subgraph, every tool call, every checkpoint, and every event.

This isn't an add-on — it's baked into the execution pipeline.

---

## RunContext

Pass identity when starting a workflow:

```csharp
var result = await runner.RunAsync(workflow, state, new RunContext
{
    TenantId = "acme-corp",
    UserId = "user-123",
    Roles = ["admin", "approver"],
    CorrelationId = "req-abc-456",
    Metadata = { ["environment"] = "production", ["region"] = "eu-west" }
});
```

| Field | Purpose |
|-------|---------|
| `TenantId` | Tenant identifier from your auth system. |
| `UserId` | User identifier from your JWT or claims. |
| `Claims` | Pass-through claims from your identity provider. |
| `Roles` | Convenience role list. Check with `HasRole("admin")`. |
| `CorrelationId` | Cross-system tracing ID. |
| `Metadata` | Arbitrary key-value pairs threaded through events and audit entries. |

`RunContext.Anonymous` is the default when no identity is provided.

---

## Where Identity Flows

Once injected, `RunContext` is automatically propagated to:

| Destination | How It's Used |
|-------------|---------------|
| **Every event** | `TenantId` and `UserId` are stamped on every `WorkflowEvent`. Filter your event sink by tenant. |
| **Every checkpoint** | Checkpoints carry `TenantId` and `UserId`. Query checkpoints scoped to a tenant. |
| **Every audit entry** | The [audit trail](../observability/audit.md) records who triggered what. |
| **Every step** | Steps receive `RunContext` via `StepContext.RunContext`. |
| **Subgraphs** | Child workflows inherit the parent's `RunContext`. |

---

## Authorization in Steps

Steps can perform role-based checks using the `RunContext`:

```csharp
public async Task<StepResult> ExecuteAsync(StepContext context)
{
    if (!context.RunContext.HasRole("approver"))
        return StepResult.Fail("User does not have the 'approver' role.");

    if (!context.RunContext.HasClaim("department", "engineering"))
        return StepResult.Fail("Only engineering department can run this step.");

    // Proceed with the operation...
}
```

Spectra never authenticates — it carries identity like `HttpContext.User` carries a `ClaimsPrincipal`. Authentication happens in your API layer; Spectra just propagates the result.

---

## Tenant-Scoped Checkpoint Queries

Because checkpoints carry `TenantId`, you can build tenant-isolated queries in your `ICheckpointStore` implementation:

```csharp
// In your custom checkpoint store:
public async Task<IReadOnlyList<Checkpoint>> ListByTenantAsync(string tenantId)
{
    return await _db.QueryAsync<Checkpoint>(
        "SELECT * FROM checkpoints WHERE tenant_id = @TenantId ORDER BY updated_at DESC",
        new { TenantId = tenantId });
}
```

This is critical for multi-tenant SaaS deployments where tenants must never see each other's workflow data.

---

## Environment-Specific Agent Overrides

Combine `RunContext` with the three-layer agent resolution to deploy the same workflow with different configurations per environment:

```csharp
// Production: use GPT-4o
var prodContext = new RunContext { Metadata = { ["env"] = "prod" } };
await runner.RunAsync(workflow, state, prodContext);

// Staging: override the agent at runtime
state.Context["__agentOverrides"] = new Dictionary<string, AgentDefinition>
{
    ["researcher"] = new AgentDefinition
    {
        Id = "researcher", Provider = "ollama", Model = "llama3",
        Temperature = 0.3, MaxTokens = 4096
    }
};
await runner.RunAsync(workflow, state, stagingContext);
```

---

## Thread Lifecycle Management

The `IThreadManager` provides full CRUD for conversation threads, scoped by tenant:

```csharp
// List threads for a specific tenant
var threads = await threadManager.ListAsync(new ThreadFilter
{
    TenantId = "acme-corp",
    UserId = "user-123"
});

// Apply retention policies
builder.AddThreadManager<InMemoryThreadManager>(new RetentionPolicy
{
    MaxMessages = 100,
    MaxAge = TimeSpan.FromDays(30)
});
```

This directly addresses a common gap in agent frameworks — thread cleanup, cloning, and tenant-scoped queries are first-class operations, not afterthoughts.

---

## What's Next

<div class="grid cards" markdown>

-   **Audit Trail**

    Tamper-evident logging with identity context.

    [:octicons-arrow-right-24: Audit Trail](../observability/audit.md)

-   **Events & Sinks**

    Identity-stamped events for observability.

    [:octicons-arrow-right-24: Events](../observability/events.md)

-   **Checkpointing**

    Tenant-scoped checkpoint persistence.

    [:octicons-arrow-right-24: Checkpointing](../execution/checkpointing.md)

</div>