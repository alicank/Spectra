# Tenant Isolation & Identity

Spectra treats caller identity as a first-class runtime primitive. A `RunContext` carrying `TenantId`, `UserId`, roles, and claims is injected at workflow invocation and automatically propagated through steps, subgraphs, checkpoints, events, and audit entries.

This isn't an add-on - it's baked into the execution pipeline. Tool calls are executed with the workflow state; tool-call events emitted by Spectra are identity-stamped, but the `ITool` interface itself does not receive `RunContext` directly.

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
| `CorrelationId` | Cross-system tracing ID available on the run context. |
| `Metadata` | Arbitrary key-value pairs available to steps through `StepContext.RunContext`. |

`RunContext.Anonymous` is the default when no identity is provided.

---

## Where Identity Flows

Once injected, `RunContext` is automatically propagated to:

| Destination | How It's Used |
|-------------|---------------|
| **Every event** | `TenantId` and `UserId` are stamped on every `WorkflowEvent`. Filter your event sink by tenant. |
| **Every checkpoint** | Checkpoints carry `TenantId` and `UserId`. Build tenant-scoped queries in your checkpoint store. |
| **Every audit entry** | The [audit trail](../observability/audit.md) records `TenantId` and `UserId` from the event or current run context. |
| **Every step** | Steps receive `RunContext` via `StepContext.RunContext`. |
| **Subgraphs** | Child workflows inherit the parent's `RunContext`. |
| **Agent tool-call events** | Tool-call events emitted by `AgentStep` are stamped with `TenantId` and `UserId`. |

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

Spectra never authenticates - it carries identity like `HttpContext.User` carries a `ClaimsPrincipal`. Authentication happens in your API layer; Spectra just propagates the result.

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

The built-in checkpoint store contract exposes `ListAsync`, `ListByRunAsync`, and related run-oriented methods. Tenant-scoped listing is a storage concern for your production `ICheckpointStore` implementation.

---

## Environment-Specific Agent Overrides

Combine `RunContext` with the agent resolution chain to deploy the same workflow with different configurations per environment:

```csharp
// Production: use the workflow's configured agents
var prodContext = new RunContext { Metadata = { ["env"] = "prod" } };
await runner.RunAsync(workflow, state, prodContext);

// Staging: override the agent at runtime
var stagingContext = new RunContext { Metadata = { ["env"] = "staging" } };
var stagingState = new WorkflowState();
stagingState.Context["__agentOverrides"] = new Dictionary<string, AgentDefinition>
{
    ["researcher"] = new AgentDefinition
    {
        Id = "researcher", Provider = "ollama", Model = "llama3",
        Temperature = 0.3, MaxTokens = 4096
    }
};
await runner.RunAsync(workflow, stagingState, stagingContext);
```

---

## Thread Lifecycle Management

The `IThreadManager` provides CRUD, cloning, retention, and bulk cleanup for conversation threads. Threads carry `TenantId` and `UserId`, and `ThreadFilter` lets you query by tenant:

```csharp
// Register the built-in in-memory manager
services.AddSpectra(builder => builder.AddInMemoryThreadManager());

// List threads for a specific tenant
var threads = await threadManager.ListAsync(new ThreadFilter
{
    TenantId = "acme-corp",
    UserId = "user-123"
});

// Apply retention policies
var retention = await threadManager.ApplyRetentionPolicyAsync(
    new RetentionPolicy
    {
        MaxCheckpointsPerThread = 100,
        MaxAge = TimeSpan.FromDays(30)
    },
    new ThreadFilter { TenantId = "acme-corp" });
```

This directly addresses a common gap in agent frameworks - thread cleanup, cloning, and tenant-scoped queries are first-class operations, not afterthoughts. Your application is still responsible for passing the right tenant filter when exposing thread APIs.

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