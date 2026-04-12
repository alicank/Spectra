---
description: "Record immutable workflow audit entries in Spectra for traceability, review, and compliance."
---

# Compliance Audit Trail

Spectra can record workflow activity into an immutable, append-only audit trail.

This gives you a durable record of:

- what the workflow did
- what the agent decided
- what tools were called
- what human responses were provided
- which tenant and user triggered the run

This is useful for:

- regulated environments
- internal review
- operational debugging
- incident investigation
- compliance and traceability

---

## What gets recorded

When audit logging is enabled, Spectra records workflow activity as `AuditEntry` records.

| What | Captured |
| --- | --- |
| **LLM calls** | Provider, model, prompt, response hash |
| **Tool invocations** | Tool name, arguments, result, duration |
| **Branching decisions** | Evaluated condition, selected edge, result |
| **Handoffs** | Source agent, target agent, intent, context |
| **Interrupts** | Pause reason, responder, response details |
| **Identity** | `TenantId`, `UserId`, and run context metadata |

This gives you a searchable explanation trail for how a run behaved.

---

## Enable audit logging

```csharp
services.AddSpectra(builder =>
{
    // In-memory
    builder.AddInMemoryAudit();

    // Or your own implementation
    builder.AddAuditStore(new PostgresAuditStore(connectionString));
});
```

Once configured, Spectra automatically adds an audit sink to the event pipeline.

You do not need to instrument individual steps manually.

---

## How it works

Audit logging builds on Spectra's event pipeline.

- workflow activity emits events
- the audit sink receives those events
- the audit store persists them as audit entries

That means audit coverage follows the same execution lifecycle as the rest of Spectra observability.

---

## `IAuditStore`

If you want your own backend, implement `IAuditStore`:

```csharp
public interface IAuditStore
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct = default);
}
```

This lets you back audit storage with:

- Postgres
- SQL Server
- Cosmos DB
- Elasticsearch
- any internal compliance system

---

## Why it matters

Audit logging is especially valuable when you need to answer questions like:

- why did this workflow take this branch?
- which tool result influenced the outcome?
- which model was used for this response?
- who approved or rejected this interrupt?
- which tenant and user initiated the run?

That is the difference between "we think this happened" and "we can show exactly what happened."

---

## Identity and audit scope

Audit entries include identity context from `RunContext`.

That makes entries queryable by:

- tenant
- user
- workflow
- run
- event type
- time range

This pairs naturally with identity-aware workflow execution and tenant isolation.

---

## A simple mental model

The audit trail is the permanent record of workflow behavior.

- **events** describe what happened
- **audit entries** persist that history for later review

That is the core idea.

---

## What's next?

<div class="grid cards" markdown>

- **Events & Sinks**

  See the event pipeline that feeds the audit store.

  [:octicons-arrow-right-24: Events](events.md)

- **OpenTelemetry**

  Add performance tracing alongside audit logging.

  [:octicons-arrow-right-24: OpenTelemetry](opentelemetry.md)

- **Tenant Isolation & Identity**

  Understand how identity flows through workflow execution.

  [:octicons-arrow-right-24: Tenant Isolation](../advanced/tenant-isolation.md)

</div>